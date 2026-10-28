using System.Globalization;

namespace SfUi.Core;

/// <summary>
/// ローカル レポートを実行してファイルへ書き出すスケジューラ ジョブ。
/// レポート定義は <see cref="ReportDefinitionStore"/>（data/reports/reports.json）から読み込む。
/// 出力形式は <see cref="ReportJobPayload.Formats"/>（例: "csv" / "excel,pdf"）。
/// </summary>
public sealed class ReportJobExecutor : IScheduledJobExecutor
{
    private readonly ILocalReportRunner _runner;
    private readonly ReportDefinitionStore _store;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public ReportJobExecutor(ILocalReportRunner runner, ReportDefinitionStore store, AppPaths paths, AppLog log)
    {
        _runner = runner;
        _store = store;
        _paths = paths;
        _log = log;
    }

    public string JobType => ScheduledJobTypes.Report;

    public async Task<JobRunResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        var payload = job.GetPayload<ReportJobPayload>();
        if (string.IsNullOrWhiteSpace(payload.ReportId))
        {
            return JobRunResult.Fail(UiText.T("Sched_ErrorNoReport"));
        }

        var definition = _store.Find(payload.ReportId);
        if (definition is null)
        {
            return JobRunResult.Fail(UiText.T("Sched_ErrorReportNotFound", payload.ReportId));
        }

        var formats = ParseFormats(payload.Formats);
        if (formats == ReportFormats.None)
        {
            return JobRunResult.Fail(UiText.T("Report_NoFormats"));
        }

        try
        {
            var targetOrg = !string.IsNullOrWhiteSpace(job.TargetOrg) ? job.TargetOrg : definition.TargetOrg;
            var result = await _runner.RunAsync(definition, targetOrg, cancellationToken).ConfigureAwait(false);
            var document = result.ToDocument();

            var baseName = BuildBaseName(payload.OutputPath, definition.DisplayName, DateTime.Now);
            var directory = ResolveDirectory(payload.OutputPath);
            var export = await ReportExportService.ExportAsync(
                document,
                new ReportExportOptions
                {
                    Formats = formats,
                    IncludeAnalysis = false,
                    MaxRows = payload.MaxRows,
                    OutputDirectory = directory,
                    BaseName = baseName,
                },
                cancellationToken).ConfigureAwait(false);

            var summary = UiText.T("Sched_SummaryReportFmt", result.Table.RowCount.ToString("N0", CultureInfo.CurrentCulture), export.Files.Count);
            var output = string.Join(Environment.NewLine, export.Files);
            return JobRunResult.Ok(summary, JobOutput.Truncate($"{result.Soql}{Environment.NewLine}{output}"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"スケジュール レポート ジョブが失敗しました ({job.DisplayName}): {ex.Message}");
            return JobRunResult.Fail(UiText.T("Sched_SummaryFailedFmt", ex.Message), ex.Message);
        }
    }

    /// <summary>"csv,excel,pdf" 等を <see cref="ReportFormats"/> へ変換する。</summary>
    public static ReportFormats ParseFormats(string? text)
    {
        var formats = ReportFormats.None;
        foreach (var token in (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            formats |= token.ToLowerInvariant() switch
            {
                "csv" => ReportFormats.Csv,
                "tsv" => ReportFormats.Tsv,
                "markdown" or "md" => ReportFormats.Markdown,
                "html" => ReportFormats.Html,
                "excel" or "xlsx" => ReportFormats.Excel,
                "pdf" => ReportFormats.Pdf,
                "json" => ReportFormats.Json,
                "jsonl" or "ndjson" => ReportFormats.JsonLines,
                "xml" => ReportFormats.Xml,
                "yaml" or "yml" => ReportFormats.Yaml,
                "png" => ReportFormats.Png,
                "jpeg" or "jpg" => ReportFormats.Jpeg,
                _ => ReportFormats.None,
            };
        }

        return formats;
    }

    /// <summary>出力フォルダー（相対パスは data/exports 基準）。空なら data/exports。</summary>
    private string ResolveDirectory(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return _paths.ExportsDirectory;
        }

        var trimmed = outputPath.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(trimmed));
            return string.IsNullOrEmpty(directory) ? _paths.ExportsDirectory : directory;
        }

        // 相対パスは「ファイル名」として扱い、data/exports 配下に置く
        var relativeDirectory = Path.GetDirectoryName(trimmed);
        return string.IsNullOrEmpty(relativeDirectory)
            ? _paths.ExportsDirectory
            : Path.Combine(_paths.ExportsDirectory, relativeDirectory);
    }

    private static string BuildBaseName(string? outputPath, string reportName, DateTime now)
    {
        var fallback = SafeFileName(reportName) + "-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return fallback;
        }

        var name = Path.GetFileNameWithoutExtension(outputPath.Trim().Trim('"'));
        return string.IsNullOrWhiteSpace(name) ? fallback : SafeFileName(name);
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        var cleaned = builder.ToString().Trim();
        return cleaned.Length == 0 ? "report" : cleaned;
    }
}
