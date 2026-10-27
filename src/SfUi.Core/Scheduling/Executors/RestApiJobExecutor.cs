using System.Net.Http;

namespace SfUi.Core;

/// <summary>任意の REST API を呼び出す Executor。</summary>
public sealed class RestApiJobExecutor : IScheduledJobExecutor
{
    private readonly SalesforceRestClient _rest;
    private readonly AppLog _log;

    public RestApiJobExecutor(SalesforceRestClient rest, AppLog log)
    {
        _rest = rest;
        _log = log;
    }

    public string JobType => ScheduledJobTypes.RestApi;

    public async Task<JobRunResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        var payload = job.GetPayload<RestApiJobPayload>();
        if (string.IsNullOrWhiteSpace(payload.Path))
        {
            return JobRunResult.Fail(UiText.T("Sched_ValidationPathRequired"));
        }

        if (string.IsNullOrWhiteSpace(job.TargetOrg))
        {
            return JobRunResult.Fail(UiText.T("Sched_ErrorNoOrg"));
        }

        var methodName = string.IsNullOrWhiteSpace(payload.Method) ? "GET" : payload.Method.Trim().ToUpperInvariant();
        var method = methodName switch
        {
            "POST" => HttpMethod.Post,
            "PATCH" => HttpMethod.Patch,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            _ => HttpMethod.Get,
        };

        var body = string.IsNullOrWhiteSpace(payload.Body) ? null : payload.Body;

        try
        {
            var (status, responseBody) = await _rest.SendConsoleAsync(job.TargetOrg, method, payload.Path.Trim(), body, cancellationToken);
            var statusCode = (int)status;
            var success = statusCode is >= 200 and < 300;
            var summary = UiText.T("Sched_SummaryRestFmt", methodName, statusCode);

            var output = new System.Text.StringBuilder()
                .AppendLine($"{methodName} {payload.Path}")
                .AppendLine($"HTTP {statusCode} {status}")
                .AppendLine(responseBody)
                .ToString();

            return success
                ? JobRunResult.Ok(summary, JobOutput.Truncate(output))
                : JobRunResult.Fail(summary, JobOutput.Truncate(FirstChars(responseBody, 500)), JobOutput.Truncate(output));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"スケジュール REST ジョブが失敗しました ({job.DisplayName}): {ex.Message}");
            return JobRunResult.Fail(UiText.T("Sched_SummaryFailedFmt", ex.Message), ex.Message);
        }
    }

    private static string FirstChars(string? text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty : (text.Length <= max ? text : text[..max]);
}
