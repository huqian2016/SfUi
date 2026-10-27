using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>スケジュール ジョブ定義と実行履歴の永続化テスト。</summary>
public class SchedulerStoreTests : IDisposable
{
    private readonly string _sandbox;

    public SchedulerStoreTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
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

    private AppPaths Paths => AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);

    private AppLog Log => new(Paths);

    private static ScheduledJob NewJob(string name = "毎朝の SOQL") => new()
    {
        Name = name,
        JobType = ScheduledJobTypes.Soql,
        TargetOrg = "hks4sand1",
        PayloadJson = "{\"query\":\"SELECT Id FROM Account\"}",
        Schedule = new JobSchedule { Kind = ScheduleKinds.Daily, TimeOfDay = "07:00" },
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
    };

    [Fact]
    public void Upsert_AssignsIdAndPersists()
    {
        var store = new ScheduledJobStore(Paths, Log);
        var job = store.Upsert(NewJob());

        Assert.False(string.IsNullOrWhiteSpace(job.Id));
        Assert.Single(store.Jobs);

        var reloaded = new ScheduledJobStore(Paths, Log);
        var saved = Assert.Single(reloaded.Jobs);
        Assert.Equal(job.Id, saved.Id);
        Assert.Equal("毎朝の SOQL", saved.Name);
        Assert.Equal(ScheduleKinds.Daily, saved.Schedule.Kind);
        Assert.Equal("07:00", saved.Schedule.TimeOfDay);
    }

    [Fact]
    public void Upsert_UpdatesExistingJob()
    {
        var store = new ScheduledJobStore(Paths, Log);
        var job = store.Upsert(NewJob());
        job.Name = "変更後";
        store.Upsert(job);

        var reloaded = new ScheduledJobStore(Paths, Log);
        var saved = Assert.Single(reloaded.Jobs);
        Assert.Equal("変更後", saved.Name);
    }

    [Fact]
    public void Delete_RemovesJob()
    {
        var store = new ScheduledJobStore(Paths, Log);
        var job = store.Upsert(NewJob());

        Assert.True(store.Delete(job.Id));
        Assert.Empty(store.Jobs);
        Assert.Empty(new ScheduledJobStore(Paths, Log).Jobs);
    }

    [Fact]
    public void GetPayload_RoundTripsSoqlQuery()
    {
        var store = new ScheduledJobStore(Paths, Log);
        var job = store.Upsert(NewJob());
        var found = store.Find(job.Id);

        Assert.NotNull(found);
        var payload = found!.GetPayload<SoqlJobPayload>();
        Assert.Equal("SELECT Id FROM Account", payload.Query);
    }

    [Fact]
    public void RunStore_AppendAndQuery_OrdersNewestFirst()
    {
        var store = new JobRunStore(Paths, Log);
        var now = DateTimeOffset.Now;

        store.Append(new JobRunRecord
        {
            JobId = "j_1",
            JobName = "A",
            JobType = ScheduledJobTypes.Soql,
            StartedAt = now.AddMinutes(-10),
            Status = JobRunStatuses.Succeeded,
            Summary = "ok",
        });
        store.Append(new JobRunRecord
        {
            JobId = "j_1",
            JobName = "A",
            JobType = ScheduledJobTypes.Soql,
            StartedAt = now,
            Status = JobRunStatuses.Failed,
            Summary = "ng",
        });

        var runs = new JobRunStore(Paths, Log).Query(10);

        Assert.Equal(2, runs.Count);
        Assert.Equal(JobRunStatuses.Failed, runs[0].Status);
        Assert.Equal(JobRunStatuses.Succeeded, runs[1].Status);
    }

    [Fact]
    public void RunStore_Prune_RemovesOldRecords()
    {
        var store = new JobRunStore(Paths, Log);
        var now = DateTimeOffset.Now;

        store.Append(new JobRunRecord
        {
            JobId = "j_1",
            JobName = "old",
            JobType = ScheduledJobTypes.Soql,
            StartedAt = now.AddDays(-120),
            Status = JobRunStatuses.Succeeded,
        });
        store.Append(new JobRunRecord
        {
            JobId = "j_1",
            JobName = "new",
            JobType = ScheduledJobTypes.Soql,
            StartedAt = now,
            Status = JobRunStatuses.Succeeded,
        });

        var deleted = store.Prune(now, retentionDays: 90, maxCount: 500);

        Assert.Equal(1, deleted);
        var remaining = Assert.Single(store.Query(10));
        Assert.Equal("new", remaining.JobName);
    }

    [Fact]
    public void RunStore_Prune_KeepsOnlyMaxCount()
    {
        var store = new JobRunStore(Paths, Log);
        var now = DateTimeOffset.Now;
        for (var i = 0; i < 5; i++)
        {
            store.Append(new JobRunRecord
            {
                JobId = "j_1",
                JobName = $"run-{i}",
                JobType = ScheduledJobTypes.Soql,
                StartedAt = now.AddMinutes(-i),
                Status = JobRunStatuses.Succeeded,
            });
        }

        var deleted = store.Prune(now, retentionDays: 90, maxCount: 2);

        Assert.Equal(3, deleted);
        var remaining = store.Query(10);
        Assert.Equal(2, remaining.Count);
        Assert.Equal("run-0", remaining[0].JobName);
        Assert.Equal("run-1", remaining[1].JobName);
    }
}
