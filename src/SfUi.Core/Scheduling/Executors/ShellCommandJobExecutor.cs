using System.Globalization;

namespace SfUi.Core;

/// <summary>
/// OS コマンドラインを実行する Executor。
/// コマンドはユーザーが自分で設定するもので、実行結果の責任はユーザーに帰属する（UI に警告を表示する）。
/// </summary>
public sealed class ShellCommandJobExecutor : IScheduledJobExecutor
{
    private readonly ShellRunner _runner;
    private readonly AppLog _log;

    public ShellCommandJobExecutor(ShellRunner runner, AppLog log)
    {
        _runner = runner;
        _log = log;
    }

    public string JobType => ScheduledJobTypes.Shell;

    public async Task<JobRunResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        var payload = job.GetPayload<ShellJobPayload>();
        if (string.IsNullOrWhiteSpace(payload.CommandLine))
        {
            return JobRunResult.Fail(UiText.T("Sched_ValidationCommandRequired"));
        }

        _log.Info($"スケジュール OS コマンドラインを実行します ({job.DisplayName}): {payload.CommandLine}");

        try
        {
            var result = await _runner.RunAsync(payload.CommandLine, payload.WorkingDirectory, timeout: null, cancellationToken);

            var summary = result.Success
                ? UiText.T("Sched_SummaryCommandOkFmt", result.Duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture))
                : result.TimedOut
                    ? UiText.T("Sched_SummaryCommandTimeoutFmt")
                    : UiText.T("Sched_SummaryCommandFailFmt", result.ExitCode);

            var output = new System.Text.StringBuilder()
                .AppendLine(result.CommandLine)
                .AppendLine($"exit={result.ExitCode} / {result.Duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)}s")
                .AppendLine();

            if (!string.IsNullOrWhiteSpace(result.StdOut))
            {
                output.AppendLine(result.StdOut.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                output.AppendLine("stderr:");
                output.AppendLine(result.StdErr.TrimEnd());
            }

            return result.Success
                ? JobRunResult.Ok(summary, JobOutput.Truncate(output.ToString()))
                : JobRunResult.Fail(summary, FirstLine(result.StdErr) ?? FirstLine(result.StdOut), JobOutput.Truncate(output.ToString()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"スケジュール OS コマンドライン ジョブが失敗しました ({job.DisplayName}): {ex.Message}");
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
