using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>組織比較ウィンドウを新しい非モーダル ウィンドウとして開く（複数同時表示対応）。</summary>
public sealed class CompareOrgsWindowFactory
{
    private readonly IServiceProvider _services;

    public CompareOrgsWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    public CompareOrgsWindow Open(IReadOnlyList<OrgInfo> orgs, Window? owner = null)
    {
        var window = _services.GetRequiredService<CompareOrgsWindow>();
        window.ViewModel.Initialize(orgs);
        // Owner は設定しない（メインウィンドウを前に出せるようにする。中央配置は自前で行う）。
        WindowPlacement.CenterOn(window, owner);
        window.Show();
        return window;
    }
}
