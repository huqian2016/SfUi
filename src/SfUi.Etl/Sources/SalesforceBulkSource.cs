using SfUi.Core;

namespace SfUi.Etl.Sources;

/// <summary>
/// Salesforce Bulk API 2.0 入力ソース: sf CLI（sf data export bulk）で SOQL の結果を CSV に書き出し、
/// その CSV を読み込む。REST クエリのページングでは厳しい大量件数の抽出に使う。
/// 列 / 行の読込みは CSV ファイル ソースと同じ規則（ヘッダー必須）。
/// </summary>
public sealed class SalesforceBulkSource : IEtlSource
{
    private readonly CsvFileSource _csv;

    /// <summary>sf CLI でエクスポートして読み込む（コンストラクターで同期実行。Bulk は時間がかかる場合がある）。</summary>
    /// <param name="runner">sf CLI ランナー。</param>
    /// <param name="targetOrg">対象組織（ユーザー名またはエイリアス）。</param>
    /// <param name="soql">SOQL クエリ（集計関数など Bulk 非対応のものは不可）。</param>
    /// <param name="workRoot">エクスポート先の親ディレクトリ（実行ごとにサブディレクトリを作る）。</param>
    public SalesforceBulkSource(SfCliRunner runner, string targetOrg, string soql, string workRoot)
        : this(
            targetOrg,
            soql,
            workRoot,
            (arguments, cancellationToken) => runner.RunAsync(
                arguments, timeout: TimeSpan.FromMinutes(31), cancellationToken: cancellationToken))
    {
    }

    /// <summary>テスト用: 任意の CLI 実行で動かす。</summary>
    internal SalesforceBulkSource(
        string targetOrg,
        string soql,
        string workRoot,
        Func<IReadOnlyList<string>, CancellationToken, Task<SfCliResult>> run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetOrg);
        ArgumentException.ThrowIfNullOrWhiteSpace(soql);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        TargetOrg = targetOrg.Trim();
        Soql = soql.Trim();
        Name = SalesforceSource.ParseObjectName(Soql) ?? "Bulk";

        // 同期コンストラクターから安全に待つ（UI スレッドから呼ばれてもデッドロックしない）
        var outputPath = Task.Run(() => ExportAsync(run, workRoot)).GetAwaiter().GetResult();
        _csv = new CsvFileSource(outputPath);
    }

    /// <summary>対象組織。</summary>
    public string TargetOrg { get; }

    /// <summary>SOQL クエリ。</summary>
    public string Soql { get; }

    /// <summary>エクスポートされた CSV のパス。</summary>
    public string OutputPath => _csv.Path;

    public string Name { get; }

    public IReadOnlyList<string> Columns => _csv.Columns;

    public IEnumerable<object?[]> ReadRows() => _csv.ReadRows();

    private async Task<string> ExportAsync(
        Func<IReadOnlyList<string>, CancellationToken, Task<SfCliResult>> run,
        string workRoot)
    {
        var directory = Path.Combine(workRoot, "bulk-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(directory);
        var outputPath = Path.Combine(directory, "export.csv");

        var arguments = new List<string>
        {
            "data", "export", "bulk",
            "--target-org", TargetOrg,
            "--query", Soql,
            "--output-file", outputPath,
            "--result-format", "csv",
            "--wait", "30",
        };

        var result = await run(arguments, CancellationToken.None).ConfigureAwait(false);
        if (!result.Success)
        {
            var reason = result.TimedOut ? "タイムアウト" : $"終了コード {result.ExitCode}";
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new InvalidOperationException($"Bulk エクスポートに失敗しました（{reason}）: {Truncate(detail.Trim(), 300)}");
        }

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException($"Bulk エクスポートの出力ファイルが作成されませんでした: {outputPath}");
        }

        return outputPath;
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";
}
