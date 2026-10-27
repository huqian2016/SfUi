using System.Diagnostics;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>スケジューラ本体（予定実行・見逃し・通知・スリープ抑止）のテスト。</summary>
public class SchedulerServiceTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly AppSettingsStore _settings;
    private readonly ScheduledJobStore _jobs;
    private readonly JobRunStore _runs;
    private readonly TestTimeProvider _time = new();
    private readonly FakeExecutor _executor = new();

    public SchedulerServiceTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
        _settings = new AppSettingsStore(_paths, _log);
        _jobs = new ScheduledJobStore(_paths, _log);
        _runs = new JobRunStore(_paths, _log);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute = 0, int second = 0) =>
        JobScheduleCalculator.AtLocal(new DateOnly(year, month, day), new TimeOnly(hour, minute, second));

    private SchedulerService CreateService(IKeepAwakeService? keepAwake = null) =>
        new(_jobs, _runs, new[] { _executor }, _settings, keepAwake ?? new NoopKeepAwakeService(), _paths, _log, _time);

    private ScheduledJob CreateJob(string timeOfDay, DateTimeOffset? nextRunAt, int graceSeconds = 60)
    {
        var job = _jobs.Upsert(new ScheduledJob
        {
            Name = "テスト ジョブ",
            JobType = ScheduledJobTypes.Soql,
            TargetOrg = "hks4sand1",
            Schedule = new JobSchedule { Kind = ScheduleKinds.Daily, TimeOfDay = timeOfDay },
            MissedGraceSeconds = graceSeconds,
            CreatedAt = _time.Now,
            UpdatedAt = _time.Now,
        });
        job.NextRunAt = nextRunAt;
        _jobs.Save();
        return job;
    }

    private static async Task WaitAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "条件が満たされませんでした（タイムアウト）");
    }

    [Fact]
    public async Task Tick_RunsDueJob_AndAdvancesNextRun()
    {
        var now = Local(2026, 10, 6, 9);
        _time.Now = now;
        var job = CreateJob("09:00", now);

        using var service = CreateService();
        await service.TickAsync();

        await WaitAsync(() => _runs.Latest(job.Id)?.Status == JobRunStatuses.Succeeded);
        Assert.Single(_executor.Calls);
        Assert.Equal(Local(2026, 10, 7, 9), _jobs.Find(job.Id)!.NextRunAt);
    }

    [Fact]
    public async Task Tick_DoesNotRunTwiceForSameOccurrence()
    {
        var now = Local(2026, 10, 6, 9);
        _time.Now = now;
        var job = CreateJob("09:00", now);

        using var service = CreateService();
        await service.TickAsync();
        await WaitAsync(() => _runs.Latest(job.Id)?.Status == JobRunStatuses.Succeeded);

        _time.Now = now.AddSeconds(1);
        await service.TickAsync();
        await Task.Delay(100);

        Assert.Single(_executor.Calls);
        Assert.Single(_runs.Query(10, job.Id));
    }

    [Fact]
    public async Task Tick_MissedRun_IsRecordedButNotExecuted()
    {
        _time.Now = Local(2026, 10, 6, 9);
        var job = CreateJob("08:00", Local(2026, 10, 6, 8));

        using var service = CreateService();
        await service.TickAsync();

        var run = _runs.Latest(job.Id);
        Assert.NotNull(run);
        Assert.Equal(JobRunStatuses.Missed, run!.Status);
        Assert.Empty(_executor.Calls);
        Assert.Equal(Local(2026, 10, 7, 8), _jobs.Find(job.Id)!.NextRunAt);
    }

    [Fact]
    public async Task Tick_RaisesPreNoticeOnlyOnce()
    {
        var now = Local(2026, 10, 6, 9);
        _time.Now = now;
        var job = CreateJob("09:00", now.AddSeconds(30));
        job.Notification.PreNoticeSeconds = 60;
        _jobs.Save();

        var notifications = new List<JobNotification>();
        using var service = CreateService();
        service.Notification += notifications.Add;

        await service.TickAsync();
        await service.TickAsync();

        var pre = Assert.Single(notifications, n => n.Kind == JobNotificationKinds.Pre);
        Assert.Equal(job.Id, pre.JobId);
        Assert.Equal(UiText.T("Sched_ToastPreTitle"), pre.Title);
    }

    [Fact]
    public async Task RunNow_EmitsStartAndFinishNotifications()
    {
        _time.Now = Local(2026, 10, 6, 9);
        var job = CreateJob("09:00", Local(2026, 10, 7, 9));

        var notifications = new List<JobNotification>();
        using var service = CreateService();
        service.Notification += notifications.Add;

        var record = await service.RunNowAsync(job.Id);

        Assert.Equal(JobRunStatuses.Succeeded, record.Status);
        Assert.Equal(JobRunTriggers.Manual, record.Trigger);
        Assert.Contains(notifications, n => n.Kind == JobNotificationKinds.Start);
        Assert.Contains(notifications, n => n.Kind == JobNotificationKinds.Finish && !n.IsError);
        Assert.Equal(JobRunStatuses.Succeeded, _jobs.Find(job.Id)!.LastRunStatus);
    }

    [Fact]
    public async Task RunNow_FailedResult_MarksRunFailed()
    {
        _time.Now = Local(2026, 10, 6, 9);
        var job = CreateJob("09:00", Local(2026, 10, 7, 9));
        _executor.Result = JobRunResult.Fail("失敗しました", "error detail");

        using var service = CreateService();
        var record = await service.RunNowAsync(job.Id);

        Assert.Equal(JobRunStatuses.Failed, record.Status);
        Assert.Equal("error detail", record.Error);
    }

    [Fact]
    public async Task Tick_KeepAwake_ActiveWhenNextRunIsNear()
    {
        _time.Now = Local(2026, 10, 6, 8, 55);
        CreateJob("09:00", Local(2026, 10, 6, 9));
        var keepAwake = new NoopKeepAwakeService();

        using var service = CreateService(keepAwake);
        await service.TickAsync();

        Assert.True(keepAwake.IsActive);
        Assert.True(service.KeepAwakeActive);
    }

    [Fact]
    public async Task Tick_KeepAwake_InactiveWhenNextRunIsFar()
    {
        _time.Now = Local(2026, 10, 6, 6);
        CreateJob("09:00", Local(2026, 10, 6, 9));
        var keepAwake = new NoopKeepAwakeService();

        using var service = CreateService(keepAwake);
        await service.TickAsync();

        Assert.False(keepAwake.IsActive);
    }

    [Fact]
    public async Task SkipNext_RecordsSkippedRun()
    {
        _time.Now = Local(2026, 10, 6, 9);
        var job = CreateJob("09:00", Local(2026, 10, 7, 9));

        using var service = CreateService();
        Assert.True(service.SkipNext(job.Id));

        var run = _runs.Latest(job.Id);
        Assert.Equal(JobRunStatuses.Skipped, run!.Status);
        Assert.Equal(Local(2026, 10, 8, 9), _jobs.Find(job.Id)!.NextRunAt);
    }

    [Fact]
    public async Task Tick_OnceJob_IsDisabledAfterRun()
    {
        _time.Now = Local(2026, 10, 6, 9);
        var job = _jobs.Upsert(new ScheduledJob
        {
            Name = "1 回だけ",
            JobType = ScheduledJobTypes.Soql,
            TargetOrg = "hks4sand1",
            Schedule = new JobSchedule { Kind = ScheduleKinds.Once, OnceAt = Local(2026, 10, 6, 9) },
            CreatedAt = _time.Now,
            UpdatedAt = _time.Now,
        });
        job.NextRunAt = Local(2026, 10, 6, 9);
        _jobs.Save();

        using var service = CreateService();
        await service.TickAsync();
        await WaitAsync(() => _runs.Latest(job.Id)?.Status == JobRunStatuses.Succeeded);

        var stored = _jobs.Find(job.Id)!;
        Assert.False(stored.Enabled);
        Assert.Null(stored.NextRunAt);
    }

    [Fact]
    public void Start_AcquiresLock_AndStopReleasesIt()
    {
        _time.Now = Local(2026, 10, 6, 9);
        CreateJob("09:00", Local(2026, 10, 7, 9));

        var first = CreateService();
        first.Start();
        Assert.True(first.IsStarted);
        first.Stop();

        var second = CreateService();
        second.Start();
        Assert.True(second.IsStarted);
        second.Stop();

        first.Dispose();
        second.Dispose();

        // ロック ファイルは残るが、解放後は別インスタンスが取得できる
        Assert.True(File.Exists(_paths.SchedulerLockFile));
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Now;

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
    }

    private sealed class FakeExecutor : IScheduledJobExecutor
    {
        public string JobType => ScheduledJobTypes.Soql;

        public List<string> Calls { get; } = new();

        public JobRunResult Result { get; set; } = JobRunResult.Ok("1 行 / 0.1 秒", "SELECT Id FROM Account");

        public Task<JobRunResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
        {
            Calls.Add(job.Id);
            return Task.FromResult(Result);
        }
    }
}
