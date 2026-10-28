namespace SfUi.Core;

/// <summary>レポート定義の入力エラー（画面表示用のメッセージ一覧を持つ）。</summary>
public sealed class ReportValidationException : Exception
{
    public ReportValidationException(IReadOnlyList<string> errors)
        : base(errors.Count > 0 ? string.Join(Environment.NewLine, errors) : UiText.T("Report_ErrorGeneric"))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>ローカル レポートの実行口（スケジューラなどから差し替えられるようにする）。</summary>
public interface ILocalReportRunner
{
    Task<LocalReportResult> RunAsync(ReportDefinition definition, string? targetOrg, CancellationToken cancellationToken = default);
}

/// <summary>
/// ローカル レポートを実行する（SOQL 生成 → 実行 → 集計）。
/// </summary>
public sealed class LocalReportRunner : ILocalReportRunner
{
    private readonly SoqlService _soql;
    private readonly AppLog _log;

    public LocalReportRunner(SoqlService soql, AppLog log)
    {
        _soql = soql;
        _log = log;
    }

    /// <summary>レポートを 1 回実行する。</summary>
    public async Task<LocalReportResult> RunAsync(
        ReportDefinition definition,
        string? targetOrg,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = ReportQueryBuilder.Validate(definition);
        if (errors.Count > 0)
        {
            throw new ReportValidationException(errors);
        }

        var org = !string.IsNullOrWhiteSpace(definition.TargetOrg) ? definition.TargetOrg!.Trim() : targetOrg?.Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            throw new ReportValidationException(new[] { UiText.T("Report_ErrorNoOrg") });
        }

        var soql = ReportQueryBuilder.Build(definition);
        _log.Info($"レポート実行: {definition.DisplayName} / {org} / {soql.Trim()}");

        var execution = await _soql.ExecuteSoqlAsync(org, soql, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ReportAggregator.Aggregate(definition, execution.Result.Table, execution.Engine, execution.Duration, soql, org);
    }
}

/// <summary>実行結果の差分（リアルタイム更新のハイライト用）。</summary>
public sealed record ReportDiff(
    IReadOnlyList<int> AddedRows,
    IReadOnlyList<int> ChangedRows,
    int RemovedRows)
{
    public static ReportDiff Empty { get; } = new(Array.Empty<int>(), Array.Empty<int>(), 0);

    public bool HasChanges => AddedRows.Count > 0 || ChangedRows.Count > 0 || RemovedRows > 0;

    public int TotalChanges => AddedRows.Count + ChangedRows.Count + RemovedRows;

    /// <summary>主キー（Id 列。無ければ全列）で 2 つの結果を比較する。</summary>
    public static ReportDiff Compute(LocalReportResult? previous, LocalReportResult? current)
    {
        if (previous is null || current is null)
        {
            return Empty;
        }

        var before = BuildSignatures(previous);
        var after = BuildSignatures(current);

        var added = new List<int>();
        var changed = new List<int>();
        var index = 0;
        foreach (var (key, signature) in after)
        {
            if (!before.TryGetValue(key, out var previousSignature))
            {
                added.Add(index);
            }
            else if (!string.Equals(previousSignature, signature, StringComparison.Ordinal))
            {
                changed.Add(index);
            }

            index++;
        }

        var removed = before.Keys.Count(key => !after.ContainsKey(key));

        // 並び順が変わっただけの行は差分にしない（キーで比較しているため）
        return new ReportDiff(added, changed, removed);
    }

    /// <summary>キー → 行内容の署名（重複キーは出現順で別扱い）。</summary>
    private static Dictionary<string, string> BuildSignatures(LocalReportResult result)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicates = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < result.RowCount; i++)
        {
            var row = result.Table.Rows[i];
            var key = KeyOf(row, result);
            if (duplicates.TryGetValue(key, out var count))
            {
                duplicates[key] = count + 1;
                key = key + "#" + (count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                duplicates[key] = 0;
            }

            map[key] = string.Join('\u0001', row.Select(cell => cell ?? string.Empty));
        }

        return map;
    }

    private static string KeyOf(IReadOnlyList<string?> row, LocalReportResult result)
    {
        for (var i = 0; i < result.Table.Columns.Count && i < row.Count; i++)
        {
            if (string.Equals(result.Table.Columns[i], "Id", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(row[i]))
            {
                return row[i]!;
            }
        }

        return "row:" + string.Join('\u0001', row.Select(cell => cell ?? string.Empty));
    }
}
