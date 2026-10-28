using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>曜日チェックボックスの 1 項目。</summary>
public sealed partial class WeekdayOption : ObservableObject
{
    public WeekdayOption(DayOfWeek day)
    {
        Day = day;
    }

    public DayOfWeek Day { get; }

    public string Label => JobSchedule.WeekdayLabel(Day);

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>ドロップダウン用の選択肢（キー + ラベル）。</summary>
public sealed record SchedulerOption(string Key, string Label)
{
    /// <summary>UIA / ログ用の表示名。</summary>
    public override string ToString() => Label;
}

/// <summary>スケジュール種別の選択肢。</summary>
public sealed record ScheduleKindOption(string Key, string Label);

/// <summary>レポート選択肢（Id + 表示名）。</summary>
public sealed record ReportOption(string Id, string Label)
{
    /// <summary>UIA / ログ用の表示名。</summary>
    public override string ToString() => Label;
}

/// <summary>レポート出力形式の選択肢。</summary>
public sealed record ReportFormatOption(string Key, string Label)
{
    /// <summary>UIA / ログ用の表示名。</summary>
    public override string ToString() => Label;
}

/// <summary>ジョブ編集ダイアログの ViewModel（全ジョブ種別対応）。</summary>
public sealed partial class SchedulerJobEditorViewModel : ObservableObject
{
    private readonly AppSettingsStore _settings;
    private readonly IFilePickerService? _filePicker;
    private readonly AppPaths? _paths;
    private readonly ReportDefinitionStore? _reports;
    private ScheduledJob? _editing;

    public SchedulerJobEditorViewModel(
        AppSettingsStore settings,
        IFilePickerService? filePicker = null,
        AppPaths? paths = null,
        ReportDefinitionStore? reports = null)
    {
        _settings = settings;
        _filePicker = filePicker;
        _paths = paths;
        _reports = reports;
        Weekdays = Enum.GetValues<DayOfWeek>()
            .OrderBy(d => d == DayOfWeek.Sunday ? 7 : (int)d)
            .Select(d => new WeekdayOption(d))
            .ToList();
    }

    /// <summary>編集対象の組織一覧。</summary>
    public ObservableCollection<OrgInfo> Orgs { get; } = new();

    /// <summary>ジョブ種別の選択肢。</summary>
    public IReadOnlyList<SchedulerOption> JobTypes { get; } =
        ScheduledJobTypes.All.Select(t => new SchedulerOption(t, ScheduledJobTypes.ToLabel(t))).ToList();

    /// <summary>スケジュール種別の選択肢。</summary>
    public IReadOnlyList<ScheduleKindOption> ScheduleKindOptions { get; } = new[]
    {
        new ScheduleKindOption(ScheduleKinds.Once, ScheduleKinds.ToLabel(ScheduleKinds.Once)),
        new ScheduleKindOption(ScheduleKinds.Daily, ScheduleKinds.ToLabel(ScheduleKinds.Daily)),
        new ScheduleKindOption(ScheduleKinds.Weekly, ScheduleKinds.ToLabel(ScheduleKinds.Weekly)),
        new ScheduleKindOption(ScheduleKinds.Monthly, ScheduleKinds.ToLabel(ScheduleKinds.Monthly)),
        new ScheduleKindOption(ScheduleKinds.Interval, ScheduleKinds.ToLabel(ScheduleKinds.Interval)),
    };

    /// <summary>データ投入の操作の選択肢。</summary>
    public IReadOnlyList<SchedulerOption> ImportOperations { get; } =
        ScheduledImportOperations.All.Select(o => new SchedulerOption(o, ScheduledImportOperations.ToLabel(o))).ToList();

    /// <summary>REST のメソッド。</summary>
    public IReadOnlyList<string> RestMethods { get; } = new[] { "GET", "POST", "PATCH", "DELETE" };

    /// <summary>エクスポート形式。</summary>
    public IReadOnlyList<string> ExportFormats { get; } = new[] { "csv", "json" };

    /// <summary>レポートの出力形式。</summary>
    public IReadOnlyList<ReportFormatOption> ReportFormatOptions { get; } = new[]
    {
        new ReportFormatOption("csv", "CSV"),
        new ReportFormatOption("excel", "Excel (xlsx)"),
        new ReportFormatOption("pdf", "PDF"),
        new ReportFormatOption("html", "HTML"),
        new ReportFormatOption("markdown", "Markdown"),
        new ReportFormatOption("json", "JSON"),
        new ReportFormatOption("tsv", "TSV"),
    };

    /// <summary>選択できるローカル レポート。</summary>
    public ObservableCollection<ReportOption> ReportChoices { get; } = new();

    /// <summary>曜日チェックボックス。</summary>
    public IReadOnlyList<WeekdayOption> Weekdays { get; }

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _jobType = ScheduledJobTypes.Soql;

    [ObservableProperty]
    private OrgInfo? _selectedOrg;

    [ObservableProperty]
    private bool _isEnabled = true;

    // ── SOQL / データ エクスポートで共用 ──
    [ObservableProperty]
    private string _soqlQuery = "";

    [ObservableProperty]
    private string _csvPath = "";

    // ── 匿名 Apex ──
    [ObservableProperty]
    private string _apexCode = "";

    // ── バックアップ ──
    [ObservableProperty]
    private string _backupObjects = "";

    [ObservableProperty]
    private string _backupLabel = "";

    [ObservableProperty]
    private string _backupDescription = "";

    // ── データ投入 ──
    [ObservableProperty]
    private string _importObject = "";

    [ObservableProperty]
    private string _importCsvPath = "";

    [ObservableProperty]
    private string _importOperation = ScheduledImportOperations.Insert;

    [ObservableProperty]
    private string _importExternalId = "";

    [ObservableProperty]
    private bool _useBulk;

    // ── データ エクスポート ──
    [ObservableProperty]
    private string _outputPath = "";

    [ObservableProperty]
    private string _exportFormat = "csv";

    // ── レポート実行 ──
    [ObservableProperty]
    private string _reportId = "";

    [ObservableProperty]
    private string _reportFormat = "csv";

    [ObservableProperty]
    private string _reportOutputPath = "";

    /// <summary>レポート出力パスの解決結果。</summary>
    public string ReportPathHint
    {
        get
        {
            if (_paths is null)
            {
                return string.Empty;
            }

            try
            {
                var resolved = JobPathResolver.ResolveOutput(ReportOutputPath, _paths);
                return UiText.T("Sched_ExportPathHintFmt", resolved.Length == 0 ? _paths.ExportsDirectory : resolved);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }

    partial void OnReportOutputPathChanged(string value) => OnPropertyChanged(nameof(ReportPathHint));

    /// <summary>出力パスの解決結果（相対パスは data/exports 配下になることを画面上に示す）。</summary>
    public string ExportPathHint
    {
        get
        {
            if (_paths is null)
            {
                return string.Empty;
            }

            try
            {
                var resolved = JobPathResolver.ResolveOutput(OutputPath, _paths);
                return UiText.T("Sched_ExportPathHintFmt", resolved.Length == 0 ? _paths.ExportsDirectory : resolved);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>出力先の「参照...」ボタンを表示できるか。]</summary>
    public bool CanBrowseExportPath => _filePicker is not null;

    partial void OnOutputPathChanged(string value) => OnPropertyChanged(nameof(ExportPathHint));

    /// <summary>出力先をファイル ダイアログで選ぶ。]</summary>
    [RelayCommand]
    private async Task BrowseExportPathAsync()
    {
        if (_filePicker is null)
        {
            return;
        }

        try
        {
            var suggested = string.IsNullOrWhiteSpace(OutputPath) ? "export.csv" : Path.GetFileName(OutputPath.Trim());
            string? initialDirectory = null;
            if (_paths is not null)
            {
                var resolved = JobPathResolver.ResolveOutput(OutputPath, _paths);
                initialDirectory = resolved.Length == 0 ? _paths.ExportsDirectory : Path.GetDirectoryName(resolved);
            }

            var picked = await _filePicker.SaveFileAsync(
                UiText.T("Sched_ExportBrowseTitle"),
                suggested,
                UiText.T("Sched_ExportBrowseFilter"),
                initialDirectory).ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(picked))
            {
                OutputPath = picked.Trim();
            }
        }
        catch (Exception)
        {
            // ダイアログが使えない環境では何もしない
        }
    }

    // ── sf コマンド ──
    [ObservableProperty]
    private string _sfArguments = "";

    // ── REST API ──
    [ObservableProperty]
    private string _restMethod = "GET";

    [ObservableProperty]
    private string _restPath = "";

    [ObservableProperty]
    private string _restBody = "";

    // ── OS コマンドライン ──
    [ObservableProperty]
    private string _shellCommandLine = "";

    [ObservableProperty]
    private string _shellWorkingDirectory = "";

    // ── スケジュール ──
    [ObservableProperty]
    private string _scheduleKind = ScheduleKinds.Once;

    [ObservableProperty]
    private DateTime? _onceDate = DateTime.Today;

    [ObservableProperty]
    private string _onceTime = "09:00";

    [ObservableProperty]
    private string _timeOfDay = "09:00";

    [ObservableProperty]
    private string _monthlyDaysText = "1";

    [ObservableProperty]
    private int _intervalMinutes = 60;

    [ObservableProperty]
    private bool _skipWeekends;

    [ObservableProperty]
    private bool _skipHolidays;

    // ── 通知 ──
    [ObservableProperty]
    private bool _notifyEnabled = true;

    [ObservableProperty]
    private int _preNoticeMinutes = 1;

    [ObservableProperty]
    private int _preNoticeSeconds;

    [ObservableProperty]
    private bool _notifyOnStart = true;

    [ObservableProperty]
    private bool _notifyOnFinish = true;

    // ── 詳細 ──
    [ObservableProperty]
    private int _timeoutMinutes = 30;

    [ObservableProperty]
    private int _missedGraceSeconds = 60;

    [ObservableProperty]
    private string _validationMessage = "";

    public bool IsSoql => JobType == ScheduledJobTypes.Soql;

    public bool IsApex => JobType == ScheduledJobTypes.Apex;

    public bool IsBackup => JobType == ScheduledJobTypes.Backup;

    public bool IsDataImport => JobType == ScheduledJobTypes.DataImport;

    public bool IsDataExport => JobType == ScheduledJobTypes.DataExport;

    public bool IsSfCommand => JobType == ScheduledJobTypes.SfCommand;

    public bool IsRestApi => JobType == ScheduledJobTypes.RestApi;

    public bool IsShell => JobType == ScheduledJobTypes.Shell;

    public bool IsReport => JobType == ScheduledJobTypes.Report;

    /// <summary>対象組織が必須の種別か。</summary>
    public bool NeedsOrg => JobType is not (ScheduledJobTypes.Shell or ScheduledJobTypes.SfCommand);

    /// <summary>SOQL 入力欄を出すか（SOQL / データ エクスポート）。</summary>
    public bool UsesSoql => IsSoql || IsDataExport;

    /// <summary>Bulk API の選択を出すか（データ投入 / エクスポート）。</summary>
    public bool UsesBulk => IsDataImport || IsDataExport;

    public bool IsOnce => ScheduleKind == ScheduleKinds.Once;

    public bool IsDaily => ScheduleKind == ScheduleKinds.Daily;

    public bool IsWeekly => ScheduleKind == ScheduleKinds.Weekly;

    public bool IsMonthly => ScheduleKind == ScheduleKinds.Monthly;

    public bool IsInterval => ScheduleKind == ScheduleKinds.Interval;

    public bool IsRecurring => !IsOnce;

    public string Title => _editing is null ? UiText.T("Sched_EditorTitleNew") : UiText.T("Sched_EditorTitleEdit");

    partial void OnJobTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSoql));
        OnPropertyChanged(nameof(IsApex));
        OnPropertyChanged(nameof(IsBackup));
        OnPropertyChanged(nameof(IsDataImport));
        OnPropertyChanged(nameof(IsDataExport));
        OnPropertyChanged(nameof(IsSfCommand));
        OnPropertyChanged(nameof(IsRestApi));
        OnPropertyChanged(nameof(IsShell));
        OnPropertyChanged(nameof(IsReport));
        OnPropertyChanged(nameof(NeedsOrg));
        OnPropertyChanged(nameof(UsesSoql));
        OnPropertyChanged(nameof(UsesBulk));

        if (value == ScheduledJobTypes.Report)
        {
            if (ReportChoices.Count == 0)
            {
                RefreshReports();
            }

            if (string.IsNullOrWhiteSpace(ReportId) && ReportChoices.Count > 0)
            {
                ReportId = ReportChoices[0].Id;
            }
        }
    }

    partial void OnScheduleKindChanged(string value)
    {
        OnPropertyChanged(nameof(IsOnce));
        OnPropertyChanged(nameof(IsDaily));
        OnPropertyChanged(nameof(IsWeekly));
        OnPropertyChanged(nameof(IsMonthly));
        OnPropertyChanged(nameof(IsInterval));
        OnPropertyChanged(nameof(IsRecurring));
    }

    /// <summary>編集対象を読み込む。</summary>
    public void Load(ScheduledJob job, IReadOnlyList<OrgInfo> orgs)
    {
        _editing = job;
        RefreshReports();
        Orgs.Clear();
        foreach (var org in orgs)
        {
            Orgs.Add(org);
        }

        Name = job.Name;
        JobType = job.JobType;
        IsEnabled = job.Enabled;
        SelectedOrg = Orgs.FirstOrDefault(o => string.Equals(o.Username, job.TargetOrg, StringComparison.OrdinalIgnoreCase))
            ?? Orgs.FirstOrDefault();

        switch (job.JobType)
        {
            case ScheduledJobTypes.Soql:
            {
                var payload = job.GetPayload<SoqlJobPayload>();
                SoqlQuery = payload.Query;
                CsvPath = payload.CsvPath ?? "";
                break;
            }

            case ScheduledJobTypes.Apex:
            {
                ApexCode = job.GetPayload<ApexJobPayload>().Code;
                break;
            }

            case ScheduledJobTypes.Backup:
            {
                var payload = job.GetPayload<BackupJobPayload>();
                BackupObjects = string.Join(",", payload.Objects);
                BackupLabel = payload.Label ?? "";
                BackupDescription = payload.Description ?? "";
                break;
            }

            case ScheduledJobTypes.DataImport:
            {
                var payload = job.GetPayload<DataImportJobPayload>();
                ImportObject = payload.ObjectName;
                ImportCsvPath = payload.CsvPath;
                ImportOperation = payload.Operation;
                ImportExternalId = payload.ExternalIdField ?? "";
                UseBulk = payload.UseBulk;
                break;
            }

            case ScheduledJobTypes.DataExport:
            {
                var payload = job.GetPayload<DataExportJobPayload>();
                SoqlQuery = payload.Soql;
                OutputPath = payload.OutputPath ?? "";
                UseBulk = payload.UseBulk;
                ExportFormat = string.IsNullOrWhiteSpace(payload.ResultFormat) ? "csv" : payload.ResultFormat;
                break;
            }

            case ScheduledJobTypes.SfCommand:
            {
                var payload = job.GetPayload<SfCommandJobPayload>();
                SfArguments = payload.Arguments;
                break;
            }

            case ScheduledJobTypes.RestApi:
            {
                var payload = job.GetPayload<RestApiJobPayload>();
                RestMethod = string.IsNullOrWhiteSpace(payload.Method) ? "GET" : payload.Method;
                RestPath = payload.Path;
                RestBody = payload.Body ?? "";
                break;
            }

            case ScheduledJobTypes.Shell:
            {
                var payload = job.GetPayload<ShellJobPayload>();
                ShellCommandLine = payload.CommandLine;
                ShellWorkingDirectory = payload.WorkingDirectory ?? "";
                break;
            }

            case ScheduledJobTypes.Report:
            {
                var payload = job.GetPayload<ReportJobPayload>();
                ReportId = payload.ReportId;
                ReportFormat = string.IsNullOrWhiteSpace(payload.Formats) ? "csv" : payload.Formats;
                ReportOutputPath = payload.OutputPath ?? "";
                break;
            }
        }

        var schedule = job.Schedule;
        ScheduleKind = schedule.Kind;
        TimeOfDay = schedule.GetTimeOfDay().ToString("HH:mm", CultureInfo.InvariantCulture);
        OnceDate = (schedule.OnceAt ?? DateTimeOffset.Now.AddHours(1)).LocalDateTime.Date;
        OnceTime = (schedule.OnceAt ?? DateTimeOffset.Now.AddHours(1)).LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        MonthlyDaysText = schedule.DaysOfMonth.Count > 0
            ? string.Join(",", schedule.DaysOfMonth)
            : "1";
        IntervalMinutes = schedule.IntervalMinutes > 0 ? schedule.IntervalMinutes : 60;
        SkipWeekends = schedule.SkipWeekends;
        SkipHolidays = schedule.SkipHolidays;
        foreach (var weekday in Weekdays)
        {
            weekday.IsSelected = schedule.DaysOfWeek.Contains(weekday.Day);
        }

        NotifyEnabled = job.Notification.Enabled;
        PreNoticeMinutes = job.Notification.PreNoticeSeconds / 60;
        PreNoticeSeconds = job.Notification.PreNoticeSeconds % 60;
        NotifyOnStart = job.Notification.NotifyOnStart;
        NotifyOnFinish = job.Notification.NotifyOnFinish;
        TimeoutMinutes = job.TimeoutSeconds > 0 ? job.TimeoutSeconds / 60 : 0;
        MissedGraceSeconds = job.MissedGraceSeconds;
        ValidationMessage = "";
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>入力内容からジョブを組み立てる（入力エラー時は null）。</summary>
    public ScheduledJob? TryBuild()
    {
        ValidationMessage = "";

        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationMessage = UiText.T("Sched_ValidationNameRequired");
            return null;
        }

        if (NeedsOrg && SelectedOrg is null)
        {
            ValidationMessage = UiText.T("Sched_ValidationOrgRequired");
            return null;
        }

        var job = _editing is null ? new ScheduledJob() : Clone(_editing);
        job.Name = Name.Trim();
        job.JobType = JobType;
        job.Enabled = IsEnabled;
        job.TargetOrg = SelectedOrg?.Username;

        if (!BuildPayload(job))
        {
            return null;
        }

        var schedule = new JobSchedule
        {
            Kind = ScheduleKind,
            SkipWeekends = SkipWeekends,
            SkipHolidays = SkipHolidays,
        };

        if (ScheduleKind == ScheduleKinds.Once)
        {
            if (!JobSchedule.TryParseTimeOfDay(OnceTime, out var onceTime))
            {
                ValidationMessage = UiText.T("Sched_ValidationPastTime");
                return null;
            }

            var date = DateOnly.FromDateTime(OnceDate ?? DateTime.Today);
            var onceAt = JobScheduleCalculator.AtLocal(date, onceTime);
            if (onceAt <= DateTimeOffset.Now)
            {
                ValidationMessage = UiText.T("Sched_ValidationPastTime");
                return null;
            }

            schedule.OnceAt = onceAt;
            schedule.TimeOfDay = onceTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        else
        {
            if (!JobSchedule.TryParseTimeOfDay(TimeOfDay, out var time))
            {
                ValidationMessage = UiText.T("Sched_ValidationPastTime");
                return null;
            }

            schedule.TimeOfDay = time.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (ScheduleKind == ScheduleKinds.Weekly)
            {
                schedule.DaysOfWeek = Weekdays.Where(w => w.IsSelected).Select(w => w.Day).ToList();
                if (schedule.DaysOfWeek.Count == 0)
                {
                    schedule.DaysOfWeek.Add(DateTime.Today.DayOfWeek);
                }
            }

            if (ScheduleKind == ScheduleKinds.Monthly)
            {
                var days = new List<int>();
                foreach (var part in MonthlyDaysText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) && day is >= 1 and <= 31)
                    {
                        days.Add(day);
                    }
                }

                schedule.DaysOfMonth = days.Count > 0 ? days : new List<int> { 1 };
            }

            if (ScheduleKind == ScheduleKinds.Interval)
            {
                if (IntervalMinutes < 1)
                {
                    ValidationMessage = UiText.T("Sched_ValidationInterval");
                    return null;
                }

                schedule.IntervalMinutes = IntervalMinutes;
                schedule.StartAt = DateTimeOffset.Now;
            }
        }

        job.Schedule = schedule;
        job.Notification = new JobNotificationSettings
        {
            Enabled = NotifyEnabled,
            PreNoticeSeconds = Math.Clamp((PreNoticeMinutes * 60) + PreNoticeSeconds, 0, 24 * 3600),
            NotifyOnStart = NotifyOnStart,
            NotifyOnFinish = NotifyOnFinish,
        };
        job.TimeoutSeconds = Math.Max(0, TimeoutMinutes) * 60;
        job.MissedGraceSeconds = Math.Clamp(MissedGraceSeconds, 0, 3600);
        return job;
    }

    /// <summary>種別ごとのペイロードを組み立てる（入力エラーで false）。</summary>
    private bool BuildPayload(ScheduledJob job)
    {
        switch (JobType)
        {
            case ScheduledJobTypes.Soql:
                if (string.IsNullOrWhiteSpace(SoqlQuery))
                {
                    ValidationMessage = UiText.T("Sched_ValidationQueryRequired");
                    return false;
                }

                job.SetPayload(new SoqlJobPayload
                {
                    Query = SoqlQuery.Trim(),
                    CsvPath = NullIfEmpty(CsvPath),
                });
                return true;

            case ScheduledJobTypes.Apex:
                if (string.IsNullOrWhiteSpace(ApexCode))
                {
                    ValidationMessage = UiText.T("Sched_ValidationApexRequired");
                    return false;
                }

                job.SetPayload(new ApexJobPayload { Code = ApexCode });
                return true;

            case ScheduledJobTypes.Backup:
            {
                var objects = BackupObjects
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (objects.Count == 0)
                {
                    ValidationMessage = UiText.T("Sched_ValidationObjectsRequired");
                    return false;
                }

                job.SetPayload(new BackupJobPayload
                {
                    Objects = objects,
                    Label = NullIfEmpty(BackupLabel),
                    Description = NullIfEmpty(BackupDescription),
                });
                return true;
            }

            case ScheduledJobTypes.DataImport:
                if (string.IsNullOrWhiteSpace(ImportObject))
                {
                    ValidationMessage = UiText.T("Sched_ValidationObjectRequired");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(ImportCsvPath))
                {
                    ValidationMessage = UiText.T("Sched_ValidationCsvRequired");
                    return false;
                }

                job.SetPayload(new DataImportJobPayload
                {
                    ObjectName = ImportObject.Trim(),
                    CsvPath = ImportCsvPath.Trim(),
                    Operation = string.IsNullOrWhiteSpace(ImportOperation) ? ScheduledImportOperations.Insert : ImportOperation,
                    ExternalIdField = NullIfEmpty(ImportExternalId),
                    UseBulk = UseBulk,
                    EmptyAsNull = true,
                });
                return true;

            case ScheduledJobTypes.DataExport:
                if (string.IsNullOrWhiteSpace(SoqlQuery))
                {
                    ValidationMessage = UiText.T("Sched_ValidationQueryRequired");
                    return false;
                }

                if (UseBulk && string.IsNullOrWhiteSpace(OutputPath))
                {
                    ValidationMessage = UiText.T("Sched_ValidationOutputRequired");
                    return false;
                }

                job.SetPayload(new DataExportJobPayload
                {
                    Soql = SoqlQuery.Trim(),
                    OutputPath = NullIfEmpty(OutputPath),
                    UseBulk = UseBulk,
                    ResultFormat = string.IsNullOrWhiteSpace(ExportFormat) ? "csv" : ExportFormat,
                });
                return true;

            case ScheduledJobTypes.SfCommand:
                if (string.IsNullOrWhiteSpace(SfArguments))
                {
                    ValidationMessage = UiText.T("Sched_ValidationCommandRequired");
                    return false;
                }

                job.SetPayload(new SfCommandJobPayload { Arguments = SfArguments.Trim() });
                return true;

            case ScheduledJobTypes.RestApi:
                if (string.IsNullOrWhiteSpace(RestPath))
                {
                    ValidationMessage = UiText.T("Sched_ValidationPathRequired");
                    return false;
                }

                job.SetPayload(new RestApiJobPayload
                {
                    Method = string.IsNullOrWhiteSpace(RestMethod) ? "GET" : RestMethod.ToUpperInvariant(),
                    Path = RestPath.Trim(),
                    Body = NullIfEmpty(RestBody),
                });
                return true;

            case ScheduledJobTypes.Shell:
                if (string.IsNullOrWhiteSpace(ShellCommandLine))
                {
                    ValidationMessage = UiText.T("Sched_ValidationCommandRequired");
                    return false;
                }

                job.SetPayload(new ShellJobPayload
                {
                    CommandLine = ShellCommandLine.Trim(),
                    WorkingDirectory = NullIfEmpty(ShellWorkingDirectory),
                });
                return true;

            case ScheduledJobTypes.Report:
                if (string.IsNullOrWhiteSpace(ReportId))
                {
                    ValidationMessage = UiText.T("Sched_ErrorNoReport");
                    return false;
                }

                job.SetPayload(new ReportJobPayload
                {
                    ReportId = ReportId.Trim(),
                    Formats = string.IsNullOrWhiteSpace(ReportFormat) ? "csv" : ReportFormat.Trim(),
                    OutputPath = NullIfEmpty(ReportOutputPath),
                });
                return true;

            default:
                ValidationMessage = UiText.T("Sched_ErrorUnknownTypeFmt", JobType);
                return false;
        }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>レポート一覧を読み直す（ダイアログを開いた時点のスナップショット）。</summary>
    public void RefreshReports()
    {
        var current = ReportId;
        ReportChoices.Clear();
        if (_reports is not null)
        {
            foreach (var report in _reports.Reports)
            {
                ReportChoices.Add(new ReportOption(report.Id, report.DisplayName));
            }
        }

        if (!string.IsNullOrWhiteSpace(current))
        {
            ReportId = current;
        }
        else if (ReportChoices.Count > 0)
        {
            ReportId = ReportChoices[0].Id;
        }
    }

    private static ScheduledJob Clone(ScheduledJob job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        Enabled = job.Enabled,
        JobType = job.JobType,
        TargetOrg = job.TargetOrg,
        PayloadJson = job.PayloadJson,
        Schedule = job.Schedule,
        Notification = job.Notification,
        TimeoutSeconds = job.TimeoutSeconds,
        MissedGraceSeconds = job.MissedGraceSeconds,
        NextRunAt = job.NextRunAt,
        LastRunAt = job.LastRunAt,
        LastRunStatus = job.LastRunStatus,
        LastRunSummary = job.LastRunSummary,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt,
    };
}
