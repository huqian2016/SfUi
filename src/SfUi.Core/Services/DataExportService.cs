using System.Data;
using System.Diagnostics;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>データエクスポート（REST ページング / Bulk API 2.0）。</summary>
public sealed class DataExportService
{
    /// <summary>REST エクスポートのページ上限（1 ページ 2,000 件前後）。</summary>
    public const int MaxPages = 500;

    private readonly SalesforceRestClient _rest;
    private readonly SfCliRunner _runner;
    private readonly AppLog _log;

    public DataExportService(SalesforceRestClient rest, SfCliRunner runner, AppLog log)
    {
        _rest = rest;
        _runner = runner;
        _log = log;
    }

    /// <summary>REST で SOQL を実行し全ページ取得する（進捗・キャンセル対応）。</summary>
    public async Task<DataExportResult> RunRestAsync(
        string targetOrg,
        string soql,
        IProgress<DataExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var records = new List<JsonElement>();
        int totalSize;
        bool done;
        var pages = 1;

        using (var document = await _rest.QueryAsync(targetOrg, soql, useToolingApi: false, cancellationToken).ConfigureAwait(false))
        {
            var root = document.RootElement;
            totalSize = GetInt(root, "totalSize") ?? 0;
            AddRecords(root, records);
            var next = GetString(root, "nextRecordsUrl");
            progress?.Report(new DataExportProgress(pages, records.Count, totalSize, "rest"));

            while (!string.IsNullOrEmpty(next) && pages < MaxPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var page = await _rest.GetPageAsync(targetOrg, next!, cancellationToken).ConfigureAwait(false);
                AddRecords(page.RootElement, records);
                next = GetString(page.RootElement, "nextRecordsUrl");
                pages++;
                progress?.Report(new DataExportProgress(pages, records.Count, totalSize, "rest"));
            }

            done = string.IsNullOrEmpty(next);
        }

        if (!done)
        {
            _log.Warn($"データエクスポート: ページ上限（{MaxPages}）に達したため残りのレコードは取得していません。");
        }

        var rawJson = JsonSerializer.Serialize(records);
        var result = SoqlResultFactory.FromJsonRecords(records, totalSize, done, rawJson);
        return new DataExportResult(result, "REST", stopwatch.Elapsed, null);
    }

    /// <summary>Bulk API 2.0 でエクスポートし、ファイルへ直接出力する（sf data export bulk）。</summary>
    public async Task<DataExportResult> RunBulkAsync(
        string targetOrg,
        string soql,
        string outputPath,
        string resultFormat,
        IProgress<DataExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var format = string.Equals(resultFormat, "json", StringComparison.OrdinalIgnoreCase) ? "json" : "csv";
        progress?.Report(new DataExportProgress(0, 0, 0, "bulk"));

        var arguments = new List<string>
        {
            "data", "export", "bulk",
            "--target-org", targetOrg,
            "--query", soql,
            "--output-file", outputPath,
            "--result-format", format,
            "--wait", "30",
            "--json",
        };

        var run = await _runner.RunAsync(arguments, timeout: TimeSpan.FromMinutes(31), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!run.Success)
        {
            throw new SalesforceApiException(DescribeFailure(run));
        }

        var (jobId, processed, _) = ImportResultMapper.ParseBulkJobInfo(run.StdOut);
        _log.Info($"Bulk エクスポート完了: {targetOrg} ジョブ {jobId ?? "(不明)"} / {processed} 件 / {outputPath}");

        SoqlResult result;
        if (format == "json")
        {
            var text = await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(text);
            var records = ExtractJsonRecords(document.RootElement);
            var raw = JsonSerializer.Serialize(records);
            result = SoqlResultFactory.FromJsonRecords(records, records.Count, true, raw);
        }
        else
        {
            var (text, _) = CsvParser.ReadFile(outputPath);
            result = BuildResultFromCsv(CsvParser.Parse(text));
        }

        progress?.Report(new DataExportProgress(0, result.RowCount, result.RowCount, "done"));
        return new DataExportResult(result, "Bulk", stopwatch.Elapsed, outputPath);
    }

    /// <summary>Bulk / CSV ファイルの内容からプレビュー用の結果を作る（純関数・テスト対象）。</summary>
    public static SoqlResult BuildResultFromCsv(CsvTable csv)
    {
        var table = new DataTable();
        foreach (var header in csv.Headers)
        {
            if (!table.Columns.Contains(header))
            {
                table.Columns.Add(header, typeof(string));
            }
        }

        foreach (var row in csv.Rows)
        {
            var dataRow = table.NewRow();
            for (var i = 0; i < csv.Headers.Count; i++)
            {
                dataRow[i] = i < row.Count && row[i].Length > 0 ? row[i] : DBNull.Value;
            }

            table.Rows.Add(dataRow);
        }

        var raw = JsonSerializer.Serialize(csv.Rows.Select(r =>
            csv.Headers.Select((h, i) => (h, v: i < r.Count ? r[i] : null)).ToDictionary(x => x.h, x => x.v)));

        return new SoqlResult
        {
            Table = table,
            TotalSize = csv.RowCount,
            Done = true,
            RawJson = raw,
        };
    }

    private static List<JsonElement> ExtractJsonRecords(JsonElement root)
    {
        var records = new List<JsonElement>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var inner))
        {
            root = inner;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                records.Add(element.Clone());
            }
        }

        return records;
    }

    private static void AddRecords(JsonElement root, List<JsonElement> records)
    {
        if (root.TryGetProperty("records", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in array.EnumerateArray())
            {
                records.Add(record.Clone());
            }
        }
    }

    private static string DescribeFailure(SfCliResult run)
    {
        if (run.TimedOut)
        {
            return UiText.T("DataIo_Err_CliTimeoutFmt", run.Duration.TotalMinutes.ToString("0.#"));
        }

        var text = (string.IsNullOrWhiteSpace(run.StdErr) ? run.StdOut : run.StdErr).Trim();
        if (text.Length > 500)
        {
            text = text[..500] + "…";
        }

        return UiText.T("DataIo_Err_CliFailedFmt", run.ExitCode, text);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
