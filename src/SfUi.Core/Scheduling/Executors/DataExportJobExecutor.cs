using System.Globalization;
using System.Text;

namespace SfUi.Core;

/// <summary>SOQL の結果をエクスポートする Executor（REST / Bulk API 2.0）。</summary>
public sealed class DataExportJobExecutor : IScheduledJobExecutor
{
    private readonly DataExportService _export;
    private readonly AppLog _log;

    public DataExportJobExecutor(DataExportService export, AppLog log)
    {
        _export = export;
        _log = log;
    }

    public string JobType => ScheduledJobTypes.DataExport;

    public async Task<JobRunResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        var payload = job.GetPayload<DataExportJobPayload>();
        if (string.IsNullOrWhiteSpace(payload.Soql))
        {
            return JobRunResult.Fail(UiText.T("Sched_ErrorNoQuery"));
        }

        if (string.IsNullOrWhiteSpace(job.TargetOrg))
        {
            return JobRunResult.Fail(UiText.T("Sched_ErrorNoOrg"));
        }

        try
        {
            var outputPath = string.IsNullOrWhiteSpace(payload.OutputPath) ? null : payload.OutputPath!.Trim();

            if (payload.UseBulk)
            {
                if (outputPath is null)
                {
                    return JobRunResult.Fail(UiText.T("Sched_ValidationOutputRequired"));
                }

                EnsureDirectory(outputPath);
                var bulk = await _export.RunBulkAsync(
                    job.TargetOrg,
                    payload.Soql,
                    outputPath,
                    string.IsNullOrWhiteSpace(payload.ResultFormat) ? "csv" : payload.ResultFormat,
                    progress: null,
                    cancellationToken);

                var writtenPath = bulk.FilePath ?? outputPath;
                var summary = UiText.T("Sched_SummaryExportFmt", bulk.Result.RowCount, writtenPath);
                return JobRunResult.Ok(
                    summary,
                    JobOutput.Truncate($"{payload.Soql}{Environment.NewLine}{writtenPath}{Environment.NewLine}rows={bulk.Result.RowCount} engine={bulk.Engine}"));
            }

            var result = await _export.RunRestAsync(job.TargetOrg, payload.Soql, progress: null, cancellationToken);

            if (outputPath is not null)
            {
                EnsureDirectory(outputPath);
                var csvText = string.Equals(Path.GetExtension(outputPath), ".json", StringComparison.OrdinalIgnoreCase)
                    ? result.Result.RawJson
                    : CsvExporter.ToCsv(result.Result.Table);
                await File.WriteAllTextAsync(outputPath, csvText, new UTF8Encoding(false), cancellationToken);
            }

            var target = outputPath ?? "-";
            var restSummary = UiText.T("Sched_SummaryExportFmt", result.Result.RowCount, target);
            var output = new StringBuilder()
                .AppendLine(payload.Soql)
                .AppendLine($"{result.Engine} / rows={result.Result.RowCount} / total={result.Result.TotalSize} / {result.Duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)}s")
                .AppendLine(target)
                .ToString();

            return JobRunResult.Ok(restSummary, JobOutput.Truncate(output));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"スケジュール データ エクスポート ジョブが失敗しました ({job.DisplayName}): {ex.Message}");
            return JobRunResult.Fail(UiText.T("Sched_SummaryFailedFmt", ex.Message), ex.Message);
        }
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
