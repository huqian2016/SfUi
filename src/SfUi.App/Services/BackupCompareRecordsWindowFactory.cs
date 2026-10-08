using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>バックアップ比較のレコード単位差分ウィンドウを生成して開く。</summary>
public sealed class BackupCompareRecordsWindowFactory
{
    private readonly IServiceProvider _services;

    public BackupCompareRecordsWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>2 つのバックアップ ID とオブジェクトを指定して開く。</summary>
    public BackupCompareRecordsWindow Open(
        string backupIdA, string backupIdB, string objectName, string displayName, OrgInfo? currentOrg = null, Window? owner = null)
    {
        var window = _services.GetRequiredService<BackupCompareRecordsWindow>();
        window.ViewModel.Initialize(backupIdA, backupIdB, objectName, displayName, currentOrg);
        // Owner は設定しない（メインウィンドウを前に出せるようにする。中央配置は自前で行う）。
        WindowPlacement.CenterOn(window, owner);
        window.Show();
        return window;
    }
}
