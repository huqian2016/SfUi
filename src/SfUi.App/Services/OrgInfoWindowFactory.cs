using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>組織情報ウィンドウを新しい非モーダル ウィンドウとして開く（複数同時表示対応）。</summary>
public sealed class OrgInfoWindowFactory
{
    private readonly IServiceProvider _services;

    public OrgInfoWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    public OrgInfoWindow Open(OrgInfo org, Window? owner = null)
    {
        var window = _services.GetRequiredService<OrgInfoWindow>();
        window.ViewModel.Initialize(org);
        // Owner は設定しない: 所有ウィンドウは常に親より前面に固定され、メインを前に出せなくなるため（中央配置は自前で行う）。
        WindowPlacement.CenterOn(window, owner);
        window.Show();
        return window;
    }
}
