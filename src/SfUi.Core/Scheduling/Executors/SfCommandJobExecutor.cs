using System.Globalization;

namespace SfUi.Core;

/// <summary>Salesforce CLI コマンドを実行する Executor。</summary>
public sealed class SfCommandJobExecutor : IScheduledJobExecutor
{
    private readonly SfCliRunner _runner;
    private readonly AppLog _log;

    public SfCommandJobExecutor(SfCliRunner runner, AppLog log)
    {
        _runner = runner;
        _log = log;
    }

    public string JobType => ScheduledJobTypes.SfCommand;

    public async Task<JobRunResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        var payload = job.GetPayload<SfCommandJobPayload>();
        var arguments = CommandLineParser.SplitSfArguments(payload.Arguments);
        if (arguments.Count == 0)
        {
            return JobRunResult.Fail(UiText.T("Sched_ErrorNoCommand"));
        }

        var workingDirectory = string.IsNullOrWhiteSpace(payload.WorkingDirectory) ? null : payload.WorkingDirectory;

        try
        {
            var result = await _runner.RunAsync(arguments, workingDirectory, cancellationToken: cancellationToken);
            var summary = result.Success
                ? UiText.T("Sched_SummaryCommandOkFmt", result.Duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture))
                : result.TimedOut
                    ? UiText.T("Sched_SummaryCommandTimeoutFmt")
                    : UiText.T("Sched_SummaryCommandFailFmt", result.ExitCode);

            var output = BuildOutput(result);
            return result.Success
                ? JobRunResult.Ok(summary, output)
                : JobRunResult.Fail(summary, FirstErrorLine(result), output);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"スケジュール sf コマンド ジョブが失敗しました ({job.DisplayName}): {ex.Message}");
            return JobRunResult.Fail(UiText.T("Sched_SummaryFailedFmt", ex.Message), ex.Message);
        }
    }

    private static string BuildOutput(SfCliResult result)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(result.CommandLine);
        builder.AppendLine($"exit={result.ExitCode} / {result.Duration.TotalSeconds:0.0}s");

        if (!string.IsNullOrWhiteSpace(result.StdOut))
        {
            builder.AppendLine();
            builder.AppendLine(result.StdOut.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            builder.AppendLine();
            builder.AppendLine("stderr:");
            builder.AppendLine(result.StdErr.TrimEnd());
        }

        return JobOutput.Truncate(builder.ToString()) ?? string.Empty;
    }

    private static string? FirstErrorLine(SfCliResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        if (string.IsNullOrWhiteSpace(text))
        {
            return result.TimedOut ? UiText.T("Sched_SummaryCommandTimeoutFmt") : null;
        }

        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return line is null ? null : JobOutput.Truncate(line);
    }
}
