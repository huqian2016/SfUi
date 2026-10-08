using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>組織管理ウィンドウを生成して開く（非モーダル・複数同時可）。</summary>
public sealed class OrgManageWindowFactory
{
    private readonly IServiceProvider _services;

    public OrgManageWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>初期選択組織を指定して開く（null 可）。</summary>
    public OrgManageWindow Open(OrgInfo? initial, Window? owner = null)
    {
        var window = _services.GetRequiredService<OrgManageWindow>();
        window.ViewModel.Initialize(initial);
        // Owner は設定しない（メインウィンドウを前に出せるようにする。中央配置は自前で行う）。
        WindowPlacement.CenterOn(window, owner);
        window.Show();
        return window;
    }
}
