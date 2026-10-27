using System.Globalization;

namespace SfUi.Core;

/// <summary>SOQL を実行し、行数と結果プレビューを返す Executor。</summary>
public sealed class SoqlJobExecutor : IScheduledJobExecutor
{
    private readonly SoqlService _soql;
    private readonly AppLog _log;

    public SoqlJobExecutor(SoqlService soql, AppLog log)
    {
        _soql = soql;
        _log = log;
    }

    public string JobType => ScheduledJobTypes.Soql;

    public async Task<JobRunResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        var payload = job.GetPayload<SoqlJobPayload>();
        if (string.IsNullOrWhiteSpace(payload.Query))
        {
            return JobRunResult.Fail(UiText.T("Sched_ErrorNoQuery"));
        }

        if (string.IsNullOrWhiteSpace(job.TargetOrg))
        {
            return JobRunResult.Fail(UiText.T("Sched_ErrorNoOrg"));
        }

        try
        {
            var execution = await _soql.ExecuteSoqlAsync(job.TargetOrg, payload.Query, cancellationToken: cancellationToken);
            var result = execution.Result;
            var summary = UiText.T(
                "Sched_SummarySoqlFmt",
                result.RowCount,
                result.TotalSize,
                execution.Duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture));

            if (!string.IsNullOrWhiteSpace(payload.CsvPath))
            {
                TryWriteCsv(payload.CsvPath!, result);
                summary = UiText.T("Sched_SummarySoqlCsvFmt", summary, payload.CsvPath!);
            }

            return JobRunResult.Ok(summary, BuildOutput(payload.Query, execution));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"スケジュール SOQL ジョブが失敗しました ({job.DisplayName}): {ex.Message}");
            return JobRunResult.Fail(UiText.T("Sched_SummaryFailedFmt", ex.Message), ex.Message);
        }
    }

    private static string BuildOutput(string query, SoqlExecution execution)
    {
        var result = execution.Result;
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(query);
        builder.AppendLine($"{execution.Engine} / {result.RowCount} rows / {execution.Duration.TotalSeconds:0.0}s");
        builder.AppendLine();

        if (result.Table.Columns.Count > 0)
        {
            builder.Append(CsvExporter.ToTsv(result.Table, includeHeader: true));
        }

        return JobOutput.Truncate(builder.ToString()) ?? string.Empty;
    }

    private void TryWriteCsv(string path, SoqlResult result)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, CsvExporter.ToCsv(result.Table), System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _log.Warn($"SOQL 結果の CSV 保存に失敗しました ({path}): {ex.Message}");
        }
    }
}
