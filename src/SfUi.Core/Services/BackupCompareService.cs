using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// バックアップ比較サービス。2 つのバックアップ（REST JSON / Bulk CSV の混在可）のレコードを
/// Id（無い場合は内容）で突合し、追加 / 削除 / 変更を返す。値はエンジン差（型 vs 文字列・空 vs null）を吸収して比較する。
/// </summary>
public sealed class BackupCompareService
{
    /// <summary>レコード差分の表示上限。</summary>
    public const int MaxDetailRows = 20_000;

    private readonly string _backupsRoot;
    private readonly AppLog _log;

    public BackupCompareService(AppPaths paths, AppLog log)
    {
        _backupsRoot = Path.Combine(paths.DataRoot, "backups");
        _log = log;
    }

    /// <summary>バックアップ一覧（新しい順）。</summary>
    public IReadOnlyList<BackupMetadata> ListBackupMetadata()
    {
        var list = new List<BackupMetadata>();
        if (!Directory.Exists(_backupsRoot))
        {
            return list;
        }

        foreach (var directory in Directory.EnumerateDirectories(_backupsRoot))
        {
            var path = Path.Combine(directory, "metadata.json");
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var metadata = JsonSerializer.Deserialize<BackupMetadata>(File.ReadAllText(path));
                if (metadata is not null)
                {
                    list.Add(metadata);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"バックアップのメタデータを読み込めません: {path} ({ex.Message})");
            }
        }

        return list.OrderByDescending(m => m.CreatedAt).ToList();
    }

    /// <summary>2 つのバックアップのオブジェクトごとの差分件数を返す。</summary>
    public async Task<BackupCompareResult> CompareAsync(
        string backupIdA,
        string backupIdB,
        IProgress<BackupCompareProgress>? progress,
        CancellationToken cancellationToken)
    {
        var metaA = LoadMetadata(backupIdA);
        var metaB = LoadMetadata(backupIdB);
        var stopwatch = Stopwatch.StartNew();
        var names = metaA.Objects.Select(o => o.Name)
            .Union(metaB.Objects.Select(o => o.Name), StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<BackupCompareObjectResult>(names.Count);
        var done = 0;
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new BackupCompareProgress(done, names.Count, name));

            var aInfo = Find(metaA, name);
            var bInfo = Find(metaB, name);
            var countA = aInfo?.Count ?? 0;
            var countB = bInfo?.Count ?? 0;
            var added = 0;
            var removed = 0;
            var changed = 0;
            string? errorA = null;
            string? errorB = null;
            try
            {
                var rowsA = await LoadRowsAsync(backupIdA, aInfo, cancellationToken).ConfigureAwait(false);
                var rowsB = await LoadRowsAsync(backupIdB, bInfo, cancellationToken).ConfigureAwait(false);
                countA = aInfo is null ? 0 : rowsA.Count;
                countB = bInfo is null ? 0 : rowsB.Count;
                (added, removed, changed) = CountDiffs(rowsA, rowsB);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"バックアップ比較 ({name}) に失敗しました", ex);
                errorA = ex.Message;
            }

            results.Add(new BackupCompareObjectResult(
                name, aInfo?.Label ?? bInfo?.Label ?? name, countA, countB,
                added, removed, changed, bInfo is null, aInfo is null, errorA, errorB));
            done++;
        }

        progress?.Report(new BackupCompareProgress(names.Count, names.Count, string.Empty));
        _log.Info($"バックアップ比較完了: {backupIdA} × {backupIdB}（{names.Count} オブジェクト / {stopwatch.Elapsed.TotalSeconds:F1} 秒）");
        return new BackupCompareResult(
            backupIdA, metaA.Label, backupIdB, metaB.Label, DateTimeOffset.Now, stopwatch.Elapsed, results);
    }

    /// <summary>1 オブジェクトのレコード単位差分を返す（表示用・上限 20,000 件）。</summary>
    public async Task<BackupCompareDetail> LoadDetailAsync(
        string backupIdA,
        string backupIdB,
        string objectName,
        CancellationToken cancellationToken)
    {
        var metaA = LoadMetadata(backupIdA);
        var metaB = LoadMetadata(backupIdB);
        var rowsA = await LoadRowsAsync(backupIdA, Find(metaA, objectName), cancellationToken).ConfigureAwait(false);
        var rowsB = await LoadRowsAsync(backupIdB, Find(metaB, objectName), cancellationToken).ConfigureAwait(false);
        var diffs = BuildDiffs(rowsA, rowsB);

        var added = diffs.Count(d => d.Kind == BackupDiffKind.Added);
        var removed = diffs.Count(d => d.Kind == BackupDiffKind.Removed);
        var changed = diffs.Count(d => d.Kind == BackupDiffKind.Changed);
        var truncated = diffs.Count > MaxDetailRows;
        var rows = truncated ? diffs.Take(MaxDetailRows).ToList() : diffs;
        return new BackupCompareDetail(added, removed, changed, rows, truncated);
    }

    // ================================================================
    // 差分計算（テスト対象の静的メソッド）
    // ================================================================

    /// <summary>追加 / 削除 / 変更の件数を返す。</summary>
    public static (int Added, int Removed, int Changed) CountDiffs(
        IReadOnlyList<Dictionary<string, object?>> rowsA,
        IReadOnlyList<Dictionary<string, object?>> rowsB)
    {
        var diffs = BuildDiffs(rowsA, rowsB, collectFields: false);
        return (
            diffs.Count(d => d.Kind == BackupDiffKind.Added),
            diffs.Count(d => d.Kind == BackupDiffKind.Removed),
            diffs.Count(d => d.Kind == BackupDiffKind.Changed));
    }

    /// <summary>レコード差分の一覧を返す（A の順 → B のみの行の順）。</summary>
    public static List<BackupRecordDiff> BuildDiffs(
        IReadOnlyList<Dictionary<string, object?>> rowsA,
        IReadOnlyList<Dictionary<string, object?>> rowsB,
        bool collectFields = true)
    {
        var mapA = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var mapB = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rowsA)
        {
            var key = KeyOf(row);
            if (!mapA.ContainsKey(key))
            {
                mapA[key] = row;
            }

            if (seen.Add(key))
            {
                order.Add(key);
            }
        }

        foreach (var row in rowsB)
        {
            var key = KeyOf(row);
            if (!mapB.ContainsKey(key))
            {
                mapB[key] = row;
            }

            if (seen.Add(key))
            {
                order.Add(key);
            }
        }

        var diffs = new List<BackupRecordDiff>();
        foreach (var key in order)
        {
            var hasA = mapA.TryGetValue(key, out var rowA);
            var hasB = mapB.TryGetValue(key, out var rowB);
            if (hasA && !hasB)
            {
                diffs.Add(new BackupRecordDiff(IdOf(rowA!), DisplayOf(rowA!), BackupDiffKind.Removed, Array.Empty<BackupFieldDiff>()));
            }
            else if (!hasA && hasB)
            {
                diffs.Add(new BackupRecordDiff(IdOf(rowB!), DisplayOf(rowB!), BackupDiffKind.Added, Array.Empty<BackupFieldDiff>()));
            }
            else if (hasA && hasB)
            {
                var fields = CompareFields(rowA!, rowB!, collectFields);
                if (fields.Count > 0)
                {
                    diffs.Add(new BackupRecordDiff(IdOf(rowA!), DisplayOf(rowA!), BackupDiffKind.Changed, fields));
                }
            }
        }

        return diffs;
    }

    /// <summary>エンジン差（型付き値 / CSV 文字列）を吸収して 2 値を比較する。</summary>
    public static bool ValuesEqual(object? valueA, object? valueB)
    {
        var textA = TextOf(valueA);
        var textB = TextOf(valueB);
        if (string.IsNullOrEmpty(textA) && string.IsNullOrEmpty(textB))
        {
            // REST の null と Bulk CSV の空文字列は同じ値として扱う
            return true;
        }

        if (textA is null || textB is null)
        {
            return false;
        }

        if (string.Equals(textA, textB, StringComparison.Ordinal))
        {
            return true;
        }

        if (decimal.TryParse(textA, NumberStyles.Number, CultureInfo.InvariantCulture, out var numberA)
            && decimal.TryParse(textB, NumberStyles.Number, CultureInfo.InvariantCulture, out var numberB))
        {
            return numberA == numberB;
        }

        if (bool.TryParse(textA, out var boolA) && bool.TryParse(textB, out var boolB))
        {
            return boolA == boolB;
        }

        if (DateTimeOffset.TryParse(textA, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateA)
            && DateTimeOffset.TryParse(textB, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateB))
        {
            return dateA == dateB;
        }

        return false;
    }

    /// <summary>表示用の文字列へ変換する（不変カルチャ）。</summary>
    public static string? TextOf(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("o", CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    // ================================================================
    // 内部
    // ================================================================

    private static BackupObjectInfo? Find(BackupMetadata metadata, string objectName) =>
        metadata.Objects.FirstOrDefault(o => string.Equals(o.Name, objectName, StringComparison.OrdinalIgnoreCase));

    private BackupMetadata LoadMetadata(string backupId)
    {
        if (!BackupService.ValidateBackupId(backupId))
        {
            throw new ArgumentException("不正なバックアップ ID です", nameof(backupId));
        }

        var path = Path.Combine(_backupsRoot, backupId, "metadata.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"バックアップのメタデータが見つかりません: {backupId}", path);
        }

        return JsonSerializer.Deserialize<BackupMetadata>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"バックアップのメタデータを読み込めません: {backupId}");
    }

    private async Task<List<Dictionary<string, object?>>> LoadRowsAsync(
        string backupId, BackupObjectInfo? info, CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object?>>();
        if (info is null || string.IsNullOrEmpty(info.File) || !BackupService.ValidateBackupId(backupId))
        {
            return rows;
        }

        var path = Path.Combine(_backupsRoot, backupId, info.File);
        if (!File.Exists(path))
        {
            return rows;
        }

        if (info.Engine == BackupEngine.Rest)
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return BackupService.ParseJsonRecords(json);
        }

        var (text, _) = CsvParser.ReadFile(path);
        var table = CsvParser.Parse(text);
        foreach (var row in table.Rows)
        {
            var record = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < table.Headers.Count; i++)
            {
                var header = table.Headers[i];
                if (header.StartsWith("sf__", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header, "attributes", StringComparison.Ordinal))
                {
                    continue;
                }

                record[header] = i < row.Count ? row[i] : null;
            }

            rows.Add(record);
        }

        return rows;
    }

    private static List<BackupFieldDiff> CompareFields(
        Dictionary<string, object?> rowA, Dictionary<string, object?> rowB, bool collect)
    {
        var names = new HashSet<string>(rowA.Keys, StringComparer.Ordinal);
        names.UnionWith(rowB.Keys);
        var result = new List<BackupFieldDiff>();
        foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (string.Equals(name, "attributes", StringComparison.Ordinal))
            {
                continue;
            }

            rowA.TryGetValue(name, out var valueA);
            rowB.TryGetValue(name, out var valueB);
            if (ValuesEqual(valueA, valueB))
            {
                continue;
            }

            if (!collect)
            {
                // 差分の有無だけ分かればよい場合は 1 件で打ち切る
                result.Add(new BackupFieldDiff(name, null, null));
                return result;
            }

            result.Add(new BackupFieldDiff(name, TextOf(valueA), TextOf(valueB)));
        }

        return result;
    }

    private static string IdOf(Dictionary<string, object?> row) =>
        row.TryGetValue("Id", out var id) ? TextOf(id) ?? string.Empty : string.Empty;

    private static string DisplayOf(Dictionary<string, object?> row)
    {
        foreach (var field in new[] { "Name", "Subject", "Title", "CaseNumber", "LastName", "Email", "DeveloperName" })
        {
            if (row.TryGetValue(field, out var value))
            {
                var text = TextOf(value);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
        }

        var id = IdOf(row);
        return string.IsNullOrEmpty(id) ? UiText.T("BackupCompare_NoId") : id;
    }

    private static string KeyOf(Dictionary<string, object?> row)
    {
        var id = IdOf(row);
        if (!string.IsNullOrEmpty(id))
        {
            return "id:" + id;
        }

        // Id が無い行（外部加工された CSV など）は全項目の内容で突合する
        var content = string.Join('\u0001', row
            .Where(pair => !string.Equals(pair.Key, "attributes", StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => TextOf(pair.Value) ?? string.Empty));
        return "row:" + content;
    }
}
