using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>ETL 移行ウィンドウを生成して開く（非モーダル・複数同時可）。</summary>
public sealed class EtlWindowFactory
{
    private readonly IServiceProvider _services;

    public EtlWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>組織（未選択でも可）を指定して開く。</summary>
    public EtlWindow Open(OrgInfo? org, Window? owner = null)
    {
        var window = _services.GetRequiredService<EtlWindow>();
        window.ViewModel.Initialize(org);
        WindowPlacement.CenterOn(window, owner);
        window.Show();
        return window;
    }
}
