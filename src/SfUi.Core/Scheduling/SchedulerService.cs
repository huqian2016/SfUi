using System.Collections.Concurrent;
using System.Globalization;

namespace SfUi.Core;

/// <summary>
/// スケジューラ本体。1 秒間隔のティックで予定時刻の判定・見逃し検出・通知発火・実行キュー投入を行う。
/// UI 非依存（イベントで通知し、表示は各 UI が担う）。
/// </summary>
public sealed class SchedulerService : IDisposable
{
    /// <summary>1 回のスキャンで 1 ジョブあたり記録する見逃し履歴の上限。</summary>
    private const int MaxMissedRecordsPerScan = 20;

    private readonly ScheduledJobStore _jobs;
    private readonly JobRunStore _runs;
    private readonly IReadOnlyDictionary<string, IScheduledJobExecutor> _executors;
    private readonly AppSettingsStore _settings;
    private readonly IKeepAwakeService _keepAwake;
    private readonly AppLog _log;
    private readonly AppPaths _paths;
    private readonly TimeProvider _time;
    private readonly IHolidayCalendar _holidays;
    private readonly SemaphoreSlim _runGate;
    private readonly SemaphoreSlim _tickGate = new(1, 1);
    private readonly ConcurrentDictionary<string, bool> _preNoticeSent = new();
    private readonly object _sync = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private FileStream? _lock;
    private int _runningCount;
    private DateTimeOffset? _nextRunAt;
    private bool _keepAwakeActive;
    private bool _disposed;

    public SchedulerService(
        ScheduledJobStore jobs,
        JobRunStore runs,
        IEnumerable<IScheduledJobExecutor> executors,
        AppSettingsStore settings,
        IKeepAwakeService keepAwake,
        AppPaths paths,
        AppLog log,
        TimeProvider? timeProvider = null,
        IHolidayCalendar? holidays = null)
    {
        _jobs = jobs;
        _runs = runs;
        _executors = executors.ToDictionary(e => e.JobType, StringComparer.OrdinalIgnoreCase);
        _settings = settings;
        _keepAwake = keepAwake;
        _paths = paths;
        _log = log;
        _time = timeProvider ?? TimeProvider.System;
        _holidays = holidays ?? JapanHolidayCalendar.Instance;
        _runGate = new SemaphoreSlim(Math.Clamp(settings.Current.SchedulerMaxConcurrent, 1, 4), 4);
    }

    /// <summary>ジョブ定義が変更されたときに発火する（UI の再描画用）。</summary>
    public event Action? JobsChanged;

    /// <summary>実行履歴の追加・更新時に発火する。</summary>
    public event Action<JobRunRecord>? RunUpdated;

    /// <summary>通知（事前 / 開始 / 終了 / 見逃し）を発火する。</summary>
    public event Action<JobNotification>? Notification;

    /// <summary>稼働状態や次回実行時刻が変わったときに発火する。</summary>
    public event Action? StateChanged;

    /// <summary>スケジューラのループが稼働中か。</summary>
    public bool IsStarted { get; private set; }

    /// <summary>別プロセスがスケジューラのロックを保持しているか。</summary>
    public bool IsLockHeldByOther { get; private set; }

    /// <summary>スリープ抑止が有効か。</summary>
    public bool KeepAwakeActive
    {
        get
        {
            lock (_sync)
            {
                return _keepAwakeActive;
            }
        }
    }

    /// <summary>有効なジョブのうち最も近い次回実行日時。</summary>
    public DateTimeOffset? NextRunAt
    {
        get
        {
            lock (_sync)
            {
                return _nextRunAt;
            }
        }
    }

    /// <summary>実行中のジョブ数。</summary>
    public int RunningCount => Volatile.Read(ref _runningCount);

    /// <summary>スケジューラを開始する（ロック取得 → 古い履歴の削除 → ループ開始）。</summary>
    public void Start()
    {
        if (_disposed || IsStarted)
        {
            return;
        }

        if (!TryAcquireLock())
        {
            IsLockHeldByOther = true;
            _log.Warn("別の SfUi がスケジューラを実行中のため、このプロセスではスケジューラを開始しません。");
            StateChanged?.Invoke();
            return;
        }

        try
        {
            _runs.Prune(_time.GetLocalNow(), _settings.Current.SchedulerRunRetentionDays, _settings.Current.SchedulerRunRetentionMax);
        }
        catch (Exception ex)
        {
            _log.Warn($"古いジョブ実行履歴の削除に失敗しました: {ex.Message}");
        }

        _cts = new CancellationTokenSource();
        IsStarted = true;
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        _ = Task.Run(() => TickSafeAsync());
        _log.Info("スケジューラを開始しました。");
        StateChanged?.Invoke();
    }

    /// <summary>スケジューラを停止する。</summary>
    public void Stop()
    {
        if (!IsStarted)
        {
            ReleaseLock();
            return;
        }

        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 既に破棄済み
        }

        IsStarted = false;
        ReleaseLock();
        _keepAwake.SetRequired(false);
        lock (_sync)
        {
            _keepAwakeActive = false;
        }

        _log.Info("スケジューラを停止しました。");
        StateChanged?.Invoke();
    }

    /// <summary>ジョブ定義が変わったことを通知し、次回実行日時を再計算する。</summary>
    public void NotifyJobsChanged()
    {
        var now = _time.GetLocalNow();
        var changed = false;
        foreach (var job in _jobs.Jobs)
        {
            if (!job.Enabled)
            {
                job.NextRunAt = null;
                continue;
            }

            if (job.NextRunAt is null)
            {
                job.NextRunAt = JobScheduleCalculator.NextOccurrence(job.Schedule, now.AddTicks(-1), now, _holidays);
                changed = true;
            }
        }

        if (changed)
        {
            _jobs.Save();
        }

        RefreshNextRunAt(now);
        JobsChanged?.Invoke();
    }

    /// <summary>手動でジョブを今すぐ実行し、完了した履歴を返す。</summary>
    public async Task<JobRunRecord> RunNowAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var job = _jobs.Find(jobId) ?? throw new InvalidOperationException(UiText.T("Sched_ErrorJobNotFoundFmt", jobId));

        var now = _time.GetLocalNow();
        var record = CreateQueuedRecord(job, now, now, JobRunTriggers.Manual);
        _runs.Append(record);
        RunUpdated?.Invoke(record);

        using var registration = cancellationToken.Register(() => record.Status = JobRunStatuses.Canceled);
        await ExecuteAsync(job, record).ConfigureAwait(false);
        return record;
    }

    /// <summary>次回予定をスキップして 1 つ先へ進める（履歴に Skipped を残す）。</summary>
    public bool SkipNext(string jobId)
    {
        var job = _jobs.Find(jobId);
        if (job?.NextRunAt is not { } next)
        {
            return false;
        }

        var now = _time.GetLocalNow();
        _runs.Append(new JobRunRecord
        {
            RunId = JobRunStore.NewRunId(job.Id, next),
            JobId = job.Id,
            JobName = job.DisplayName,
            JobType = job.JobType,
            TargetOrg = job.TargetOrg,
            Trigger = JobRunTriggers.Schedule,
            ScheduledAt = next,
            StartedAt = next,
            FinishedAt = now,
            Status = JobRunStatuses.Skipped,
            Summary = UiText.T("Sched_SkippedSummary"),
        });

        job.NextRunAt = JobScheduleCalculator.NextOccurrence(job.Schedule, next, now, _holidays);
        job.UpdatedAt = now;
        if (job.NextRunAt is null && job.Schedule.Kind == ScheduleKinds.Once)
        {
            job.Enabled = false;
        }

        _jobs.Save();
        RefreshNextRunAt(now);
        JobsChanged?.Invoke();
        return true;
    }

    /// <summary>1 ティック分の判定を実行する（テスト用にも公開）。</summary>
    public async Task TickAsync()
    {
        if (!await _tickGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await TickCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _tickGate.Release();
        }
    }

    private async Task TickCoreAsync()
    {
        if (!_settings.Current.SchedulerEnabled)
        {
            UpdateKeepAwake(force: false);
            return;
        }

        var now = _time.GetLocalNow();
        var dirty = false;

        foreach (var job in _jobs.Jobs)
        {
            if (!job.Enabled)
            {
                continue;
            }

            if (job.NextRunAt is null)
            {
                job.NextRunAt = JobScheduleCalculator.NextOccurrence(job.Schedule, now.AddTicks(-1), now, _holidays);
                if (job.NextRunAt is null)
                {
                    job.Enabled = false;
                }

                dirty = true;
                continue;
            }

            var next = job.NextRunAt.Value;
            var grace = TimeSpan.FromSeconds(Math.Max(0, job.MissedGraceSeconds));

            // 見逃し: 猶予を超えて過ぎた予定は実行せず Missed として記録する
            if (now > next + grace)
            {
                RecordMissed(job, now);
                dirty = true;
                continue;
            }

            // 事前通知
            var notify = job.Notification;
            if (notify is { Enabled: true, PreNoticeSeconds: > 0 })
            {
                var preAt = next - TimeSpan.FromSeconds(notify.PreNoticeSeconds);
                var key = job.Id + "|" + next.UtcTicks.ToString(CultureInfo.InvariantCulture);
                if (now >= preAt && now < next && _preNoticeSent.TryAdd(key, true))
                {
                    Raise(new JobNotification
                    {
                        Kind = JobNotificationKinds.Pre,
                        JobId = job.Id,
                        JobName = job.DisplayName,
                        Title = UiText.T("Sched_ToastPreTitle"),
                        Message = UiText.T(
                            "Sched_ToastPreMessageFmt",
                            job.DisplayName,
                            next.LocalDateTime.ToString("HH:mm", CultureInfo.CurrentCulture),
                            Math.Max(1, (int)Math.Ceiling((next - now).TotalMinutes))),
                        At = now,
                    });
                }
            }

            // 予定時刻（猶予内）
            if (now >= next)
            {
                var record = CreateQueuedRecord(job, next, now, JobRunTriggers.Schedule);
                _runs.Append(record);
                RunUpdated?.Invoke(record);
                _ = Task.Run(() => ExecuteAsync(job, record));

                job.NextRunAt = JobScheduleCalculator.NextOccurrence(job.Schedule, next, now, _holidays);
                if (job.NextRunAt is null && job.Schedule.Kind == ScheduleKinds.Once)
                {
                    job.Enabled = false;
                }

                job.UpdatedAt = now;
                dirty = true;
            }
        }

        if (dirty)
        {
            _jobs.Save();
        }

        RefreshNextRunAt(now);
        UpdateKeepAwake(force: false);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void RecordMissed(ScheduledJob job, DateTimeOffset now)
    {
        var recorded = 0;
        var cursor = job.NextRunAt;

        while (cursor is { } scheduled && now > scheduled + TimeSpan.FromSeconds(Math.Max(0, job.MissedGraceSeconds)) && recorded < MaxMissedRecordsPerScan)
        {
            _runs.Append(new JobRunRecord
            {
                RunId = JobRunStore.NewRunId(job.Id, scheduled),
                JobId = job.Id,
                JobName = job.DisplayName,
                JobType = job.JobType,
                TargetOrg = job.TargetOrg,
                Trigger = JobRunTriggers.Schedule,
                ScheduledAt = scheduled,
                StartedAt = scheduled,
                FinishedAt = now,
                Status = JobRunStatuses.Missed,
                Summary = UiText.T("Sched_MissedSummary"),
            });

            recorded++;
            cursor = JobScheduleCalculator.NextOccurrence(job.Schedule, scheduled, now, _holidays);
        }

        // 上限を超えた分はまとめて読み飛ばす（次回予定は現在時刻より後に）
        if (cursor is { } remaining && now > remaining + TimeSpan.FromSeconds(Math.Max(0, job.MissedGraceSeconds)))
        {
            cursor = JobScheduleCalculator.NextOccurrence(job.Schedule, now, now, _holidays);
        }

        job.NextRunAt = cursor;
        if (job.NextRunAt is null && job.Schedule.Kind == ScheduleKinds.Once)
        {
            job.Enabled = false;
        }

        job.UpdatedAt = now;

        if (recorded > 0)
        {
            Raise(new JobNotification
            {
                Kind = JobNotificationKinds.Missed,
                JobId = job.Id,
                JobName = job.DisplayName,
                Title = UiText.T("Sched_ToastMissedTitle"),
                Message = UiText.T("Sched_ToastMissedMessageFmt", job.DisplayName, recorded),
                At = now,
                IsError = true,
            });
        }
    }

    private JobRunRecord CreateQueuedRecord(ScheduledJob job, DateTimeOffset scheduledAt, DateTimeOffset now, string trigger) =>
        new()
        {
            RunId = JobRunStore.NewRunId(job.Id, now),
            JobId = job.Id,
            JobName = job.DisplayName,
            JobType = job.JobType,
            TargetOrg = job.TargetOrg,
            Trigger = trigger,
            ScheduledAt = scheduledAt,
            StartedAt = now,
            Status = JobRunStatuses.Queued,
            Summary = UiText.T("Sched_QueuedSummary"),
        };

    private async Task ExecuteAsync(ScheduledJob job, JobRunRecord record)
    {
        await _runGate.WaitAsync().ConfigureAwait(false);
        Interlocked.Increment(ref _runningCount);
        UpdateKeepAwake(force: true);

        var startedAt = _time.GetLocalNow();
        try
        {
            record.Status = JobRunStatuses.Running;
            record.StartedAt = startedAt;
            record.Summary = UiText.T("Sched_RunningSummary");
            _runs.Update(record);
            RunUpdated?.Invoke(record);

            if (job.Notification is { Enabled: true, NotifyOnStart: true })
            {
                Raise(new JobNotification
                {
                    Kind = JobNotificationKinds.Start,
                    JobId = job.Id,
                    JobName = job.DisplayName,
                    Title = UiText.T("Sched_ToastStartTitle"),
                    Message = UiText.T("Sched_ToastStartMessageFmt", job.DisplayName),
                    At = startedAt,
                });
            }

            using var timeout = job.TimeoutSeconds > 0
                ? new CancellationTokenSource(TimeSpan.FromSeconds(job.TimeoutSeconds), _time)
                : new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                timeout.Token,
                _cts?.Token ?? CancellationToken.None);

            JobRunResult result;
            try
            {
                if (!_executors.TryGetValue(job.JobType, out var executor))
                {
                    result = JobRunResult.Fail(UiText.T("Sched_ErrorUnknownTypeFmt", job.JobType));
                }
                else
                {
                    result = await executor.ExecuteAsync(job, linked.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                result = JobRunResult.Fail(UiText.T("Sched_SummaryTimeoutFmt"), UiText.T("Sched_SummaryTimeoutFmt"));
            }
            catch (OperationCanceledException)
            {
                result = JobRunResult.Fail(UiText.T("Sched_SummaryCanceledFmt"), UiText.T("Sched_SummaryCanceledFmt"));
            }
            catch (Exception ex)
            {
                _log.Error($"スケジュール ジョブの実行に失敗しました ({job.DisplayName})", ex);
                result = JobRunResult.Fail(UiText.T("Sched_SummaryFailedFmt", ex.Message), ex.Message);
            }

            var finishedAt = _time.GetLocalNow();
            record.FinishedAt = finishedAt;
            record.DurationSeconds = Math.Max(0, (finishedAt - startedAt).TotalSeconds);
            record.Status = result.Success ? JobRunStatuses.Succeeded : JobRunStatuses.Failed;
            record.Summary = result.Summary;
            record.Error = result.Error;
            record.OutputPreview = result.Output;

            job.LastRunAt = finishedAt;
            job.LastRunStatus = record.Status;
            job.LastRunSummary = record.Summary;
            job.UpdatedAt = finishedAt;

            _runs.Update(record);
            _jobs.Save();

            if (job.Notification is { Enabled: true, NotifyOnFinish: true })
            {
                Raise(new JobNotification
                {
                    Kind = JobNotificationKinds.Finish,
                    JobId = job.Id,
                    JobName = job.DisplayName,
                    Title = result.Success ? UiText.T("Sched_ToastDoneTitle") : UiText.T("Sched_ToastFailedTitle"),
                    Message = UiText.T("Sched_ToastDoneMessageFmt", job.DisplayName, record.Summary),
                    At = finishedAt,
                    IsError = !result.Success,
                });
            }
        }
        finally
        {
            Interlocked.Decrement(ref _runningCount);
            _runGate.Release();
            RunUpdated?.Invoke(record);
            UpdateKeepAwake(force: true);
            JobsChanged?.Invoke();
        }
    }

    private void Raise(JobNotification notification)
    {
        try
        {
            Notification?.Invoke(notification);
        }
        catch (Exception ex)
        {
            _log.Error("スケジューラ通知の配信中に例外が発生しました", ex);
        }
    }

    private void RefreshNextRunAt(DateTimeOffset now)
    {
        DateTimeOffset? earliest = null;
        foreach (var job in _jobs.Jobs)
        {
            if (!job.Enabled || job.NextRunAt is not { } next)
            {
                continue;
            }

            if (next < now)
            {
                continue;
            }

            if (earliest is null || next < earliest)
            {
                earliest = next;
            }
        }

        bool changed;
        lock (_sync)
        {
            changed = _nextRunAt != earliest;
            _nextRunAt = earliest;
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }
    }

    private void UpdateKeepAwake(bool force)
    {
        var required = false;
        if (_settings.Current.SchedulerEnabled && _settings.Current.SchedulerKeepAwake)
        {
            var now = _time.GetLocalNow();
            var lead = TimeSpan.FromMinutes(Math.Max(0, _settings.Current.SchedulerKeepAwakeLeadMinutes));
            required = RunningCount > 0 || (NextRunAt is { } next && next - now <= lead && next >= now.AddTicks(-1) && next - now > TimeSpan.FromMinutes(-5));
        }

        bool changed;
        lock (_sync)
        {
            changed = _keepAwakeActive != required;
            _keepAwakeActive = required;
        }

        _keepAwake.SetRequired(required);

        if (changed || force)
        {
            StateChanged?.Invoke();
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await TickSafeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 停止要求
        }
        catch (Exception ex)
        {
            _log.Error("スケジューラ ループが停止しました", ex);
        }
    }

    private async Task TickSafeAsync()
    {
        try
        {
            await TickAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error("スケジューラのティックで例外が発生しました", ex);
        }
    }

    private bool TryAcquireLock()
    {
        try
        {
            var directory = Path.GetDirectoryName(_paths.SchedulerLockFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _lock = new FileStream(_paths.SchedulerLockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException ex)
        {
            _log.Warn($"スケジューラのロックを取得できませんでした: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Warn($"スケジューラのロック ファイルにアクセスできませんでした: {ex.Message}");
            return false;
        }
    }

    private void ReleaseLock()
    {
        try
        {
            _lock?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warn($"スケジューラのロック解放に失敗しました: {ex.Message}");
        }
        finally
        {
            _lock = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        try
        {
            _cts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // 無視
        }

        _cts = null;
        _keepAwake.Release();
        _runningCount = 0;
    }
}
