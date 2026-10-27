using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>ジョブ編集ダイアログの抽象化（実装は各 UI プロジェクト）。</summary>
public interface ISchedulerJobEditor
{
    /// <summary>ジョブを編集する。キャンセル時は null。</summary>
    SchedulerJobEditResult? Edit(ScheduledJob job, IReadOnlyList<OrgInfo> orgs);
}

/// <summary>ジョブ編集の結果。</summary>
public sealed record SchedulerJobEditResult(ScheduledJob Job, bool RunNow);

/// <summary>カレンダー 1 日分のセル。</summary>
public sealed partial class SchedulerCalendarDay : ObservableObject
{
    public SchedulerCalendarDay(DateOnly date, bool isCurrentMonth, bool isToday, bool isHoliday)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        IsToday = isToday;
        IsHoliday = isHoliday;
    }

    public DateOnly Date { get; }

    public bool IsCurrentMonth { get; }

    public bool IsToday { get; }

    public bool IsHoliday { get; }

    /// <summary>日付の数字。</summary>
    public string DayText => Date.Day.ToString(CultureInfo.InvariantCulture);

    /// <summary>日曜か（赤字表示用）。</summary>
    public bool IsSunday => Date.DayOfWeek == DayOfWeek.Sunday;

    /// <summary>土曜か。</summary>
    public bool IsSaturday => Date.DayOfWeek == DayOfWeek.Saturday;

    /// <summary>その日の予定。</summary>
    public ObservableCollection<SchedulerJobRow> Jobs { get; } = new();

    /// <summary>その日の予定があるか。</summary>
    public bool HasJobs => Jobs.Count > 0;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(SelectionBrushKey));

    /// <summary>選択状態の表示キー（XAML の DataTrigger 用）。</summary>
    public string SelectionBrushKey => IsSelected ? "Selected" : "Normal";
}

/// <summary>カレンダー / 一覧に表示する 1 ジョブ。</summary>
public sealed class SchedulerJobRow
{
    public SchedulerJobRow(ScheduledJob job, DateTimeOffset? occurrence = null)
    {
        Job = job;
        Occurrence = occurrence;
    }

    public ScheduledJob Job { get; }

    /// <summary>カレンダー上の発火時刻（カレンダー以外では null）。</summary>
    public DateTimeOffset? Occurrence { get; }

    public string Name => Job.DisplayName;

    public string JobId => Job.Id;

    public string TypeLabel => ScheduledJobTypes.ToLabel(Job.JobType);

    public string ScheduleLabel => Job.Schedule.Describe();

    public string TimeText => (Occurrence ?? Job.NextRunAt)?.LocalDateTime.ToString("HH:mm", CultureInfo.CurrentCulture) ?? "--:--";

    public string NextRunText => Job.NextRunAt is { } next
        ? next.LocalDateTime.ToString("yyyy/MM/dd HH:mm", CultureInfo.CurrentCulture)
        : UiText.T("Sched_StatusBarNone");

    public string LastRunText => Job.LastRunAt is { } last
        ? $"{last.LocalDateTime:yyyy/MM/dd HH:mm} {JobRunStatuses.ToLabel(Job.LastRunStatus ?? "")}".Trim()
        : "—";

    public string OrgLabel => Job.TargetOrg ?? "—";

    public string StatusBadge => Job.Enabled ? UiText.T("Sched_EnabledBadge") : UiText.T("Sched_DisabledBadge");

    public bool IsEnabled => Job.Enabled;

    public string LastRunStatusLabel => Job.LastRunStatus is null ? "—" : JobRunStatuses.ToLabel(Job.LastRunStatus);

    public string LastRunSummary => Job.LastRunSummary ?? "";
}

/// <summary>スケジュール ウィンドウの ViewModel（カレンダー / 実行履歴）。</summary>
public sealed partial class SchedulerViewModel : ObservableObject
{
    private readonly SchedulerService _scheduler;
    private readonly ScheduledJobStore _jobStore;
    private readonly JobRunStore _runStore;
    private readonly AppSettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ISchedulerJobEditor _editor;
    private readonly IUiDispatcher _ui;
    private readonly AppLog _log;

    private IReadOnlyList<OrgInfo> _orgs = Array.Empty<OrgInfo>();
    private DateOnly _monthAnchor = DateOnly.FromDateTime(DateTime.Today);

    public SchedulerViewModel(
        SchedulerService scheduler,
        ScheduledJobStore jobStore,
        JobRunStore runStore,
        AppSettingsStore settings,
        IDialogService dialogs,
        ISchedulerJobEditor editor,
        NotificationCenterViewModel notificationCenter,
        IUiDispatcher ui,
        AppLog log)
    {
        _scheduler = scheduler;
        _jobStore = jobStore;
        _runStore = runStore;
        _settings = settings;
        _dialogs = dialogs;
        _editor = editor;
        _ui = ui;
        _log = log;
        Notifications = notificationCenter;
        SelectedDate = DateOnly.FromDateTime(DateTime.Today);

        _scheduler.JobsChanged += OnJobsChanged;
        _scheduler.RunUpdated += OnRunUpdated;
        _scheduler.StateChanged += OnStateChanged;

        Refresh();
        RefreshRuns();
    }

    /// <summary>通知センター（フェード ポップアップ）。</summary>
    public NotificationCenterViewModel Notifications { get; }

    /// <summary>月カレンダーの 42 セル。</summary>
    public ObservableCollection<SchedulerCalendarDay> Days { get; } = new();

    /// <summary>選択日の予定。</summary>
    public ObservableCollection<SchedulerJobRow> SelectedDayJobs { get; } = new();

    /// <summary>実行履歴（新しい順）。</summary>
    public ObservableCollection<JobRunRecord> Runs { get; } = new();

    /// <summary>登録済みジョブ一覧（有効 / 無効を問わずすべて）。</summary>
    public IReadOnlyList<SchedulerJobRow> AllJobs => _allJobs;

    private List<SchedulerJobRow> _allJobs = new();

    [ObservableProperty]
    private DateOnly _selectedDate;

    /// <summary>選択日の見出し（例: 2026/10/07 (水)）。</summary>
    public string SelectedDayHeader =>
        SelectedDate.ToDateTime(TimeOnly.MinValue).ToString("yyyy/MM/dd (ddd)", CultureInfo.CurrentCulture);

    partial void OnSelectedDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(SelectedDayHeader));
        RefreshSelectedDay();
    }

    [ObservableProperty]
    private SchedulerJobRow? _selectedJob;

    [ObservableProperty]
    private JobRunRecord? _selectedRun;

    [ObservableProperty]
    private string _monthText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _nextRunText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLockHeldByOther;

    [ObservableProperty]
    private bool _isKeepAwakeActive;

    /// <summary>曜日ヘッダー（日〜土）。</summary>
    public IReadOnlyList<string> WeekdayHeaders { get; } = new[]
    {
        DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
    }.Select(JobSchedule.WeekdayLabel).ToList();

    /// <summary>現在の組織一覧を設定する（ウィンドウ生成時に呼ぶ）。</summary>
    public void Initialize(IReadOnlyList<OrgInfo> orgs, OrgInfo? currentOrg)
    {
        _orgs = orgs;
        if (currentOrg is not null)
        {
            _defaultOrg = currentOrg.Username;
        }

        Refresh();
    }

    private string? _defaultOrg;

    /// <summary>表示を最新化する。</summary>
    public void Refresh()
    {
        BuildCalendar();
        RefreshRuns();
        UpdateStatus();
        OnPropertyChanged(nameof(AllJobs));
    }

    /// <summary>実行履歴を再読み込みする。</summary>
    public void RefreshRuns()
    {
        Runs.Clear();
        foreach (var run in _runStore.Query(Math.Max(50, _settings.Current.SchedulerRunRetentionMax)))
        {
            Runs.Add(run);
        }

        if (SelectedRun is not null)
        {
            SelectedRun = Runs.FirstOrDefault(r => r.RunId == SelectedRun.RunId);
        }
    }

    private void BuildCalendar()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var current = SelectedDate != default ? SelectedDate : today;
        var first = new DateOnly(_monthAnchor.Year, _monthAnchor.Month, 1);
        var offset = (int)first.DayOfWeek; // 日曜始まり
        var start = first.AddDays(-offset);

        Days.Clear();
        var now = DateTimeOffset.Now;
        var jobs = _jobStore.Jobs.Where(j => j.Enabled).ToList();

        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            var day = new SchedulerCalendarDay(
                date,
                date.Month == _monthAnchor.Month && date.Year == _monthAnchor.Year,
                date == today,
                JapanHolidayCalendar.Instance.IsHoliday(date));

            var dayStart = JobScheduleCalculator.AtLocal(date, new TimeOnly(0, 0));
            var dayEnd = dayStart.AddDays(1).AddTicks(-1);
            foreach (var job in jobs)
            {
                var occurrences = JobScheduleCalculator.OccurrencesBetween(job.Schedule, dayStart.AddTicks(-1), dayEnd, now, null, 8);
                foreach (var occurrence in occurrences)
                {
                    day.Jobs.Add(new SchedulerJobRow(job, occurrence));
                }
            }

            day.IsSelected = date == current;
            Days.Add(day);
        }

        MonthText = UiText.T("Sched_MonthFmt", _monthAnchor.Year, _monthAnchor.Month);
        _allJobs = _jobStore.Jobs
            .Select(j => new SchedulerJobRow(j))
            .OrderBy(r => r.Name, StringComparer.CurrentCulture)
            .ToList();
        RefreshSelectedDay();
    }

    private void RefreshSelectedDay()
    {
        SelectedDayJobs.Clear();
        var day = Days.FirstOrDefault(d => d.Date == SelectedDate);
        if (day is null)
        {
            return;
        }

        foreach (var row in day.Jobs.OrderBy(r => r.Occurrence))
        {
            SelectedDayJobs.Add(row);
        }
    }

    private void SelectDate(SchedulerCalendarDay day)
    {
        SelectedDate = day.Date;
        foreach (var item in Days)
        {
            item.IsSelected = item.Date == day.Date;
        }

        _monthAnchor = day.Date;
        RefreshSelectedDay();
        OnPropertyChanged(nameof(AllJobs));
    }

    private void UpdateStatus()
    {
        IsLockHeldByOther = _scheduler.IsLockHeldByOther;
        IsKeepAwakeActive = _scheduler.KeepAwakeActive;

        NextRunText = _scheduler.NextRunAt is { } next
            ? UiText.T("Sched_StatusBarFmt", next.LocalDateTime.ToString("MM/dd HH:mm", CultureInfo.CurrentCulture), "")
            : UiText.T("Sched_StatusBarNone");
        StatusText = IsLockHeldByOther
            ? UiText.T("Sched_LockHeldByOther")
            : IsKeepAwakeActive
                ? UiText.T("Sched_KeepAwake")
                : UiText.T("Common_Ready");
    }

    private void OnJobsChanged() => _ui.Post(() =>
    {
        BuildCalendar();
        UpdateStatus();
        OnPropertyChanged(nameof(AllJobs));
    });

    private void OnRunUpdated(JobRunRecord record) => _ui.Post(() =>
    {
        var existing = Runs.FirstOrDefault(r => r.RunId == record.RunId);
        if (existing is null)
        {
            Runs.Insert(0, record);
        }
        else
        {
            var index = Runs.IndexOf(existing);
            Runs[index] = record;
        }

        UpdateStatus();
    });

    private void OnStateChanged() => _ui.Post(UpdateStatus);

    [RelayCommand]
    private void PrevMonth()
    {
        _monthAnchor = _monthAnchor.AddMonths(-1);
        BuildCalendar();
    }

    [RelayCommand]
    private void NextMonth()
    {
        _monthAnchor = _monthAnchor.AddMonths(1);
        BuildCalendar();
    }

    [RelayCommand]
    private void GoToday()
    {
        _monthAnchor = DateOnly.FromDateTime(DateTime.Today);
        SelectedDate = _monthAnchor;
        BuildCalendar();
    }

    [RelayCommand]
    private void SelectDay(SchedulerCalendarDay? day)
    {
        if (day is not null)
        {
            SelectDate(day);
        }
    }

    [RelayCommand]
    private void RefreshAll()
    {
        Refresh();
        Notify("Common_Ready");
    }

    [RelayCommand]
    private void NewJob()
    {
        var job = new ScheduledJob
        {
            JobType = ScheduledJobTypes.Soql,
            TargetOrg = _defaultOrg ?? _orgs.FirstOrDefault()?.Username,
            Notification = new JobNotificationSettings
            {
                Enabled = _settings.Current.SchedulerNotifyDefault,
                PreNoticeSeconds = (_settings.Current.SchedulerPreNoticeMinutes * 60) + _settings.Current.SchedulerPreNoticeSeconds,
            },
            MissedGraceSeconds = _settings.Current.SchedulerMissedGraceSeconds,
            Schedule = new JobSchedule
            {
                Kind = ScheduleKinds.Once,
                OnceAt = DateTimeOffset.Now.AddHours(1),
            },
        };

        EditJobCore(job, isNew: true);
    }

    [RelayCommand]
    private void EditJob(SchedulerJobRow? row)
    {
        if (row is not null)
        {
            EditJobCore(row.Job, isNew: false);
        }
    }

    private void EditJobCore(ScheduledJob job, bool isNew)
    {
        SchedulerJobEditResult? result;
        try
        {
            result = _editor.Edit(job, _orgs);
        }
        catch (Exception ex)
        {
            _log.Error("ジョブ編集ダイアログを開けませんでした", ex);
            Notify("Common_FailedFmt", ex.Message);
            return;
        }

        if (result is null)
        {
            return;
        }

        var edited = result.Job;
        var now = DateTimeOffset.Now;
        if (string.IsNullOrWhiteSpace(edited.Id))
        {
            edited.Id = ScheduledJob.NewId();
            edited.CreatedAt = now;
        }

        edited.UpdatedAt = now;
        edited.NextRunAt = ComputeNextRun(edited, now);
        if (edited.Schedule.Kind == ScheduleKinds.Once && edited.NextRunAt is null)
        {
            edited.Enabled = false;
        }

        _jobStore.Upsert(edited);
        _scheduler.NotifyJobsChanged();
        BuildCalendar();
        OnPropertyChanged(nameof(AllJobs));
        SelectedJob = AllJobs.FirstOrDefault(r => r.JobId == edited.Id);
        Notify("Common_SavedFmt", edited.DisplayName);

        if (result.RunNow)
        {
            _ = RunJobAsync(edited.Id);
        }
    }

    private static DateTimeOffset? ComputeNextRun(ScheduledJob job, DateTimeOffset now)
    {
        if (job.Schedule.Kind == ScheduleKinds.Once)
        {
            return job.Schedule.OnceAt is { } at && at > now ? at : null;
        }

        return JobScheduleCalculator.NextOccurrence(job.Schedule, now, now, null);
    }

    [RelayCommand]
    private void DeleteJob(SchedulerJobRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (!_dialogs.ConfirmDestructive(
                UiText.T("Sched_ConfirmDeleteFmt", row.Name),
                UiText.T("Sched_ConfirmDeleteTitle")))
        {
            return;
        }

        _jobStore.Delete(row.JobId);
        _scheduler.NotifyJobsChanged();
        SelectedJob = null;
        BuildCalendar();
        OnPropertyChanged(nameof(AllJobs));
    }

    [RelayCommand]
    private void DuplicateJob(SchedulerJobRow? row)
    {
        if (row is null)
        {
            return;
        }

        var copy = Clone(row.Job);
        copy.Id = ScheduledJob.NewId();
        copy.Name = row.Name + " (2)";
        copy.CreatedAt = DateTimeOffset.Now;
        copy.UpdatedAt = copy.CreatedAt;
        copy.LastRunAt = null;
        copy.LastRunStatus = null;
        copy.LastRunSummary = null;
        copy.NextRunAt = ComputeNextRun(copy, copy.CreatedAt);
        _jobStore.Upsert(copy);
        _scheduler.NotifyJobsChanged();
        BuildCalendar();
        OnPropertyChanged(nameof(AllJobs));
    }

    private static ScheduledJob Clone(ScheduledJob job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        Enabled = job.Enabled,
        JobType = job.JobType,
        TargetOrg = job.TargetOrg,
        PayloadJson = job.PayloadJson,
        Schedule = new JobSchedule
        {
            Kind = job.Schedule.Kind,
            OnceAt = job.Schedule.OnceAt,
            TimeOfDay = job.Schedule.TimeOfDay,
            DaysOfWeek = new List<DayOfWeek>(job.Schedule.DaysOfWeek),
            DaysOfMonth = new List<int>(job.Schedule.DaysOfMonth),
            IntervalMinutes = job.Schedule.IntervalMinutes,
            StartAt = job.Schedule.StartAt,
            EndAt = job.Schedule.EndAt,
            SkipWeekends = job.Schedule.SkipWeekends,
            SkipHolidays = job.Schedule.SkipHolidays,
        },
        Notification = new JobNotificationSettings
        {
            Enabled = job.Notification.Enabled,
            PreNoticeSeconds = job.Notification.PreNoticeSeconds,
            NotifyOnStart = job.Notification.NotifyOnStart,
            NotifyOnFinish = job.Notification.NotifyOnFinish,
        },
        TimeoutSeconds = job.TimeoutSeconds,
        MissedGraceSeconds = job.MissedGraceSeconds,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt,
    };

    [RelayCommand]
    private void ToggleEnabled(SchedulerJobRow? row)
    {
        if (row is null)
        {
            return;
        }

        var job = _jobStore.Find(row.JobId);
        if (job is null)
        {
            return;
        }

        job.Enabled = !job.Enabled;
        job.NextRunAt = job.Enabled ? ComputeNextRun(job, DateTimeOffset.Now) : null;
        job.UpdatedAt = DateTimeOffset.Now;
        _jobStore.Upsert(job);
        _scheduler.NotifyJobsChanged();
        BuildCalendar();
        OnPropertyChanged(nameof(AllJobs));
    }

    [RelayCommand]
    private void SkipNext(SchedulerJobRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (_scheduler.SkipNext(row.JobId))
        {
            BuildCalendar();
            RefreshRuns();
        }
    }

    [RelayCommand]
    private Task RunNow(SchedulerJobRow? row) => row is null ? Task.CompletedTask : RunJobAsync(row.JobId);

    private async Task RunJobAsync(string jobId)
    {
        if (IsBusy)
        {
            Notify("Sched_StatusRunning");
            return;
        }

        IsBusy = true;
        try
        {
            var record = await _scheduler.RunNowAsync(jobId).ConfigureAwait(true);
            RefreshRuns();
            SelectedRun = Runs.FirstOrDefault(r => r.RunId == record.RunId);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _log.Error("ジョブを手動実行できませんでした", ex);
            Notify("Common_FailedFmt", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectRun(JobRunRecord? run)
    {
        if (run is not null)
        {
            SelectedRun = run;
            SelectedJob = AllJobs.FirstOrDefault(r => r.JobId == run.JobId);
        }
    }

    private void Notify(string key, params object[] args) =>
        StatusText = args.Length == 0 ? UiText.T(key) : UiText.T(key, args);
}
