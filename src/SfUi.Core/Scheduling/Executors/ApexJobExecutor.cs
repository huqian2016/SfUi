namespace SfUi.Core;

/// <summary>匿名 Apex を実行する Executor。</summary>
public sealed class ApexJobExecutor : IScheduledJobExecutor
{
    private readonly ApexService _apex;
    private readonly AppLog _log;

    public ApexJobExecutor(ApexService apex, AppLog log)
    {
        _apex = apex;
        _log = log;
    }

    public string JobType => ScheduledJobTypes.Apex;

    public async Task<JobRunResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        var payload = job.GetPayload<ApexJobPayload>();
        if (string.IsNullOrWhiteSpace(payload.Code))
        {
            return JobRunResult.Fail(UiText.T("Sched_ValidationApexRequired"));
        }

        if (string.IsNullOrWhiteSpace(job.TargetOrg))
        {
            return JobRunResult.Fail(UiText.T("Sched_ErrorNoOrg"));
        }

        try
        {
            var result = await _apex.ExecuteAnonymousAsync(job.TargetOrg, payload.Code, cancellationToken: cancellationToken);

            var output = new System.Text.StringBuilder();
            output.AppendLine(job.TargetOrg);
            output.AppendLine($"compiled={result.Compiled} success={result.Success}");

            if (!string.IsNullOrWhiteSpace(result.CompileProblem))
            {
                output.AppendLine();
                output.AppendLine("compile:");
                output.AppendLine(result.CompileProblem);
            }

            if (!string.IsNullOrWhiteSpace(result.ExceptionMessage))
            {
                output.AppendLine();
                output.AppendLine("exception:");
                output.AppendLine(result.ExceptionMessage);
                if (!string.IsNullOrWhiteSpace(result.ExceptionStackTrace))
                {
                    output.AppendLine(result.ExceptionStackTrace);
                }
            }

            if (result.Success)
            {
                return JobRunResult.Ok(UiText.T("Sched_SummaryApexOkFmt"), JobOutput.Truncate(output.ToString()));
            }

            var reason = FirstLine(result.CompileProblem) ?? FirstLine(result.ExceptionMessage) ?? UiText.T("Sched_SummaryApexFailFmt", string.Empty);
            var summary = result.Compiled
                ? UiText.T("Sched_SummaryApexFailFmt", reason)
                : UiText.T("Sched_SummaryApexCompileFmt", reason);

            return JobRunResult.Fail(JobOutput.Truncate(summary) ?? summary, reason, JobOutput.Truncate(output.ToString()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"スケジュール匿名 Apex ジョブが失敗しました ({job.DisplayName}): {ex.Message}");
            return JobRunResult.Fail(UiText.T("Sched_SummaryFailedFmt", ex.Message), ex.Message);
        }
    }

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }
}
