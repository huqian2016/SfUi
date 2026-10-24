using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>バックアップと復元ウィンドウの親 ViewModel（組織を 2 タブで共有する）。</summary>
public sealed partial class BackupViewModel : ObservableObject, IDisposable
{
    private bool _initialized;
    private readonly IReportExportDialog _reportDialog;

    public BackupViewModel(BackupTabViewModel backup, RestoreTabViewModel restore, CompareTabViewModel compare, IReportExportDialog reportDialog, AppLog log)
    {
        Backup = backup;
        Restore = restore;
        Compare = compare;
        _reportDialog = reportDialog;
        Backup.BackupCompleted += () =>
        {
            Restore.RefreshBackups();
            Compare.RefreshBackups();
        };
    }

    public BackupTabViewModel Backup { get; }

    public RestoreTabViewModel Restore { get; }

    public CompareTabViewModel Compare { get; }

    public OrgInfo? Org { get; private set; }

    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>ウィンドウ生成時に組織を受け取る。</summary>
    public void Initialize(OrgInfo org)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Org = org;
        Title = UiText.T("Backup_TitleFmt", org.DisplayName);
        Backup.Initialize(org);
        Restore.Initialize(org);
        Compare.Initialize(org);
    }

    /// <summary>ウィンドウ表示後に 1 回呼ぶ。</summary>
    public async Task LoadAsync()
    {
        await Backup.LoadAsync();
        Restore.RefreshBackups();
        Compare.RefreshBackups();
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        var columns = new[]
        {
            UiText.T("Report_BackupColId"),
            UiText.T("Report_BackupColLabel"),
            UiText.T("Report_BackupColSummary"),
            UiText.T("Report_BackupColDescription"),
        };
        var rows = Restore.Backups
            .Select(item => (IReadOnlyList<string?>)new[] { item.Id, item.Title, item.Summary, item.Description })
            .ToList();
        var table = WindowReportBuilder.Table(UiText.T("Report_BackupSection"), columns, rows);
        var section = WindowReportBuilder.Section(table.Name, table);
        if (section is null)
        {
            return;
        }

        var document = WindowReportBuilder.Document(
            UiText.T("Report_BackupTitle"),
            null,
            Org is null ? null : (string.IsNullOrWhiteSpace(Org.Alias) ? Org.Username : Org.Alias),
            new[] { section });
        await ReportExportRunner.RunAsync(_reportDialog, document, "backup", null);
    }

    public void Dispose()
    {
        Backup.CancelBackgroundWork();
        Restore.CancelBackgroundWork();
        Compare.CancelBackgroundWork();
    }
}
