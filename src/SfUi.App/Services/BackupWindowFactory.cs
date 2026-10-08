using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>バックアップと復元ウィンドウを生成して開く（非モーダル・複数同時可）。</summary>
public sealed class BackupWindowFactory
{
    private readonly IServiceProvider _services;

    public BackupWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>組織を指定して開く。</summary>
    public BackupWindow Open(OrgInfo org, Window? owner = null)
    {
        var window = _services.GetRequiredService<BackupWindow>();
        window.ViewModel.Initialize(org);
        // Owner は設定しない（メインウィンドウを前に出せるようにする。中央配置は自前で行う）。
        WindowPlacement.CenterOn(window, owner);
        window.Show();
        return window;
    }
}
