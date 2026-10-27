using SfUi.App.ViewModels;
using SfUi.Core;
using SfUi.Presentation;
using Xunit;

namespace SfUi.Tests;

/// <summary>スケジュール ウィンドウ / 通知センター / ジョブ編集の ViewModel テスト。</summary>
[Collection("Localization")]
public class SchedulerViewModelTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly AppSettingsStore _settings;
    private readonly ScheduledJobStore _jobs;
    private readonly JobRunStore _runs;
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeEditor _editor = new();
    private readonly FakeDispatcher _ui = new();
    private readonly FakeExecutor _executor = new();
    private readonly SchedulerService _scheduler;

    public SchedulerViewModelTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
        _settings = new AppSettingsStore(_paths, _log);
        _jobs = new ScheduledJobStore(_paths, _log);
        _runs = new JobRunStore(_paths, _log);
        _scheduler = new SchedulerService(
            _jobs,
            _runs,
            new[] { _executor },
            _settings,
            new NoopKeepAwakeService(),
            _paths,
            _log);
    }

    public void Dispose()
    {
        _scheduler.Dispose();
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

    private SchedulerViewModel CreateViewModel() =>
        new(_scheduler, _jobs, _runs, _settings, _dialogs, _editor, new NotificationCenterViewModel(_ui), _ui, _log);

    private ScheduledJob CreateJob(string name = "毎朝の SOQL", bool enabled = true)
    {
        var job = _jobs.Upsert(new ScheduledJob
        {
            Name = name,
            Enabled = enabled,
            JobType = ScheduledJobTypes.Soql,
            TargetOrg = "hks4sand1",
            Schedule = new JobSchedule { Kind = ScheduleKinds.Daily, TimeOfDay = "00:00" },
            NextRunAt = DateTimeOffset.Now.AddHours(1),
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        });
        return job;
    }

    [Fact]
    public void Initialize_BuildsSixWeekCalendar()
    {
        var viewModel = CreateViewModel();

        viewModel.Initialize(Array.Empty<OrgInfo>(), null);

        Assert.Equal(42, viewModel.Days.Count);
        Assert.Equal(7, viewModel.WeekdayHeaders.Count);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.MonthText));
    }

    [Fact]
    public void SelectedDay_ContainsOccurrencesOfEnabledJobs()
    {
        CreateJob();
        var viewModel = CreateViewModel();

        viewModel.Initialize(Array.Empty<OrgInfo>(), null);

        Assert.Single(viewModel.SelectedDayJobs);
        Assert.Single(viewModel.AllJobs);
    }

    [Fact]
    public void DisabledJob_IsNotShownOnCalendar_ButStaysInJobList()
    {
        CreateJob(enabled: false);
        var viewModel = CreateViewModel();

        viewModel.Initialize(Array.Empty<OrgInfo>(), null);

        Assert.Empty(viewModel.SelectedDayJobs);
        Assert.Single(viewModel.AllJobs);
    }

    [Fact]
    public void NewJob_AddsThroughEditor()
    {
        var viewModel = CreateViewModel();
        viewModel.Initialize(Array.Empty<OrgInfo>(), null);
        _editor.Result = new SchedulerJobEditResult(new ScheduledJob
        {
            Name = "新しいジョブ",
            JobType = ScheduledJobTypes.Soql,
            TargetOrg = "hks4sand1",
            Schedule = new JobSchedule { Kind = ScheduleKinds.Once, OnceAt = DateTimeOffset.Now.AddHours(2) },
        }, RunNow: false);

        viewModel.NewJobCommand.Execute(null);

        var job = Assert.Single(_jobs.Jobs);
        Assert.Equal("新しいジョブ", job.Name);
        Assert.NotNull(job.NextRunAt);
        Assert.Single(viewModel.AllJobs);
    }

    [Fact]
    public void DeleteJob_Confirmed_RemovesJob()
    {
        var job = CreateJob();
        var viewModel = CreateViewModel();
        viewModel.Initialize(Array.Empty<OrgInfo>(), null);
        _dialogs.Result = true;

        viewModel.DeleteJobCommand.Execute(viewModel.AllJobs.Single());

        Assert.Empty(_jobs.Jobs);
        Assert.Null(_jobs.Find(job.Id));
    }

    [Fact]
    public void DeleteJob_Canceled_KeepsJob()
    {
        CreateJob();
        var viewModel = CreateViewModel();
        viewModel.Initialize(Array.Empty<OrgInfo>(), null);
        _dialogs.Result = false;

        viewModel.DeleteJobCommand.Execute(viewModel.AllJobs.Single());

        Assert.Single(_jobs.Jobs);
    }

    [Fact]
    public void ToggleEnabled_FlipsFlag()
    {
        var job = CreateJob();
        var viewModel = CreateViewModel();
        viewModel.Initialize(Array.Empty<OrgInfo>(), null);

        viewModel.ToggleEnabledCommand.Execute(viewModel.AllJobs.Single());

        var stored = _jobs.Find(job.Id)!;
        Assert.False(stored.Enabled);
        Assert.Null(stored.NextRunAt);
    }

    [Fact]
    public async Task RunNow_RecordsHistoryAndSelectsRun()
    {
        var job = CreateJob();
        var viewModel = CreateViewModel();
        viewModel.Initialize(Array.Empty<OrgInfo>(), null);

        await viewModel.RunNowCommand.ExecuteAsync(viewModel.AllJobs.Single());

        var run = Assert.Single(viewModel.Runs);
        Assert.Equal(job.Id, run.JobId);
        Assert.Equal(JobRunStatuses.Succeeded, run.Status);
        Assert.Equal(run.RunId, viewModel.SelectedRun?.RunId);
    }

    [Fact]
    public void Editor_RequiresNameAndQuery()
    {
        var editor = new SchedulerJobEditorViewModel(_settings);
        editor.Load(new ScheduledJob { JobType = ScheduledJobTypes.Soql, TargetOrg = "hks4sand1" }, Array.Empty<OrgInfo>());

        Assert.Null(editor.TryBuild());
        Assert.Equal(UiText.T("Sched_ValidationNameRequired"), editor.ValidationMessage);

        editor.Name = "ジョブ";
        editor.SelectedOrg = new OrgInfo("hks4sand1", "acc", "00D", "https://example", "Connected", false, true);
        Assert.Null(editor.TryBuild());
        Assert.Equal(UiText.T("Sched_ValidationQueryRequired"), editor.ValidationMessage);

        editor.SoqlQuery = "SELECT Id FROM Account";
        var job = editor.TryBuild();
        Assert.NotNull(job);
        Assert.Equal("SELECT Id FROM Account", job!.GetPayload<SoqlJobPayload>().Query);
    }

    [Fact]
    public void Editor_BuildsWeeklySchedule()
    {
        var editor = new SchedulerJobEditorViewModel(_settings);
        editor.Load(new ScheduledJob { JobType = ScheduledJobTypes.Soql, TargetOrg = "hks4sand1" }, Array.Empty<OrgInfo>());
        editor.Name = "週次";
        editor.SelectedOrg = new OrgInfo("hks4sand1", "acc", "00D", "https://example", "Connected", false, true);
        editor.SoqlQuery = "SELECT Id FROM Account";
        editor.ScheduleKind = ScheduleKinds.Weekly;
        editor.TimeOfDay = "07:30";
        foreach (var weekday in editor.Weekdays)
        {
            weekday.IsSelected = weekday.Day == DayOfWeek.Monday;
        }

        var job = editor.TryBuild();

        Assert.NotNull(job);
        Assert.Equal(ScheduleKinds.Weekly, job!.Schedule.Kind);
        Assert.Equal("07:30", job.Schedule.TimeOfDay);
        Assert.Equal(new[] { DayOfWeek.Monday }, job.Schedule.DaysOfWeek);
    }

    [Fact]
    public void Editor_RejectsPastOnceTime()
    {
        var editor = new SchedulerJobEditorViewModel(_settings);
        editor.Load(new ScheduledJob { JobType = ScheduledJobTypes.Soql, TargetOrg = "hks4sand1" }, Array.Empty<OrgInfo>());
        editor.Name = "過去";
        editor.SelectedOrg = new OrgInfo("hks4sand1", "acc", "00D", "https://example", "Connected", false, true);
        editor.SoqlQuery = "SELECT Id FROM Account";
        editor.ScheduleKind = ScheduleKinds.Once;
        editor.OnceDate = DateTime.Today.AddDays(-1);

        Assert.Null(editor.TryBuild());
        Assert.Equal(UiText.T("Sched_ValidationPastTime"), editor.ValidationMessage);
    }

    [Fact]
    public void NotificationCenter_EnqueueAndDismiss()
    {
        var center = new NotificationCenterViewModel(_ui);

        center.Enqueue(new JobNotification
        {
            Kind = JobNotificationKinds.Finish,
            JobId = "j_1",
            JobName = "テスト",
            Title = "実行完了",
            Message = "テスト: 1 行",
            At = DateTimeOffset.Now,
        });

        var item = Assert.Single(center.Notifications);
        Assert.Equal("実行完了", item.Title);
        Assert.True(center.HasNotifications);

        center.Remove(item);

        Assert.Empty(center.Notifications);
        Assert.False(center.HasNotifications);
    }

    [Fact]
    public void NotificationCenter_KeepsAtMostThreeItems()
    {
        var center = new NotificationCenterViewModel(_ui);
        for (var i = 0; i < 5; i++)
        {
            center.Enqueue(new JobNotification { Kind = JobNotificationKinds.Finish, Title = $"n{i}", Message = "" });
        }

        Assert.Equal(NotificationCenterViewModel.MaxVisible, center.Notifications.Count);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool Result { get; set; }

        public void Info(string message, string caption)
        {
        }

        public void Warning(string message, string caption)
        {
        }

        public bool Confirm(string message, string caption) => Result;

        public bool ConfirmDestructive(string message, string caption) => Result;

        public string? Prompt(string title, string prompt, string defaultValue = "") => null;
    }

    private sealed class FakeEditor : ISchedulerJobEditor
    {
        public SchedulerJobEditResult? Result { get; set; }

        public SchedulerJobEditResult? Edit(ScheduledJob job, IReadOnlyList<OrgInfo> orgs) => Result;
    }

    private sealed class FakeDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public void Invoke(Action action) => action();
    }

    private sealed class FakeExecutor : IScheduledJobExecutor
    {
        public string JobType => ScheduledJobTypes.Soql;

        public Task<JobRunResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken) =>
            Task.FromResult(JobRunResult.Ok("1 行 / 0.1 秒"));
    }
}
