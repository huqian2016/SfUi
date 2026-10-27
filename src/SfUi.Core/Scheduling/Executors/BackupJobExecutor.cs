using System.Globalization;

namespace SfUi.Core;

/// <summary>オブジェクト単位のバックアップを実行する Executor。</summary>
public sealed class BackupJobExecutor : IScheduledJobExecutor
{
    private readonly BackupService _backup;
    private readonly AppLog _log;

    public BackupJobExecutor(BackupService backup, AppLog log)
    {
        _backup = backup;
        _log = log;
    }

    public string JobType => ScheduledJobTypes.Backup;

    public async Task<JobRunResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        var payload = job.GetPayload<BackupJobPayload>();
        var objects = payload.Objects
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (objects.Count == 0)
        {
            return JobRunResult.Fail(UiText.T("Sched_ValidationObjectsRequired"));
        }

        if (string.IsNullOrWhiteSpace(job.TargetOrg))
        {
            return JobRunResult.Fail(UiText.T("Sched_ErrorNoOrg"));
        }

        var org = job.TargetOrg;
        var label = string.IsNullOrWhiteSpace(payload.Label)
            ? UiText.T("Sched_BackupDefaultLabelFmt", org, DateTime.Now)
            : payload.Label!.Trim();
        var description = string.IsNullOrWhiteSpace(payload.Description)
            ? UiText.T("Sched_BackupDefaultDescriptionFmt", job.DisplayName)
            : payload.Description!.Trim();

        try
        {
            var metadata = await _backup.RunBackupAsync(
                org,
                org,
                org,
                null,
                AppVersion(),
                label,
                description,
                objects,
                progress: null,
                cancellationToken);

            var summary = UiText.T(
                "Sched_SummaryBackupFmt",
                metadata.Objects.Count,
                metadata.TotalRecords,
                metadata.Id);

            var output = new System.Text.StringBuilder();
            output.AppendLine($"backup={metadata.Id} label={metadata.Label}");
            foreach (var item in metadata.Objects)
            {
                output.AppendLine($"{item.Name}: {item.Count.ToString("N0", CultureInfo.CurrentCulture)}");
            }

            return JobRunResult.Ok(summary, JobOutput.Truncate(output.ToString()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"スケジュール バックアップ ジョブが失敗しました ({job.DisplayName}): {ex.Message}");
            return JobRunResult.Fail(UiText.T("Sched_SummaryFailedFmt", ex.Message), ex.Message);
        }
    }

    private static string AppVersion()
    {
        var version = typeof(BackupJobExecutor).Assembly.GetName().Version;
        return version is null ? "0.0.0" : version.ToString(3);
    }
}
