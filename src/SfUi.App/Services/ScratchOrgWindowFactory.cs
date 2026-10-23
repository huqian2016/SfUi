using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>スクラッチ組織ウィンドウを生成して開く（非モーダル・複数同時可）。</summary>
public sealed class ScratchOrgWindowFactory
{
    private readonly IServiceProvider _services;

    public ScratchOrgWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>メイン ウィンドウで選択中の組織を Dev Hub の初期値にして開く。</summary>
    public ScratchOrgWindow Open(OrgInfo? initial, Window? owner = null)
    {
        var window = _services.GetRequiredService<ScratchOrgWindow>();
        window.ViewModel.Initialize(initial);
        WindowPlacement.CenterOn(window, owner);
        window.Show();
        return window;
    }
}
