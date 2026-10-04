using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>バックアップと復元ウィンドウの親 ViewModel（組織を 2 タブで共有する）。</summary>
public sealed partial class BackupViewModel : ObservableObject, IDisposable
{
    private bool _initialized;

    public BackupViewModel(BackupTabViewModel backup, RestoreTabViewModel restore, AppLog log)
    {
        Backup = backup;
        Restore = restore;
        Backup.BackupCompleted += () => Restore.RefreshBackups();
    }

    public BackupTabViewModel Backup { get; }

    public RestoreTabViewModel Restore { get; }

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
    }

    /// <summary>ウィンドウ表示後に 1 回呼ぶ。</summary>
    public async Task LoadAsync()
    {
        await Backup.LoadAsync();
        Restore.RefreshBackups();
    }

    public void Dispose()
    {
        Backup.CancelBackgroundWork();
        Restore.CancelBackgroundWork();
    }
}
