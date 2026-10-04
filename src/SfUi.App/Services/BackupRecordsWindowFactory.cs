using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>レコード詳細（バックアップ内のレコード一覧）ウィンドウを生成して開く。</summary>
public sealed class BackupRecordsWindowFactory
{
    private readonly IServiceProvider _services;

    public BackupRecordsWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>バックアップ ID（と現在の組織）を指定して開く。</summary>
    public BackupRecordsWindow Open(string backupId, BackupObjectInfo info, string displayName, OrgInfo? currentOrg = null, Window? owner = null)
    {
        var window = _services.GetRequiredService<BackupRecordsWindow>();
        window.ViewModel.Initialize(backupId, info, displayName, currentOrg);
        if (owner is not null && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }

        window.Show();
        return window;
    }
}
