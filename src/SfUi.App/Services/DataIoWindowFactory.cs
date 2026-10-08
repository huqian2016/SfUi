using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>データ入出力ウィンドウを生成して開く（非モーダル・複数同時可）。</summary>
public sealed class DataIoWindowFactory
{
    private readonly IServiceProvider _services;

    public DataIoWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>組織（と任意で対象オブジェクト・SOQL）を指定して開く。</summary>
    public DataIoWindow Open(OrgInfo org, string? objectApiName = null, string? soql = null, Window? owner = null)
    {
        var window = _services.GetRequiredService<DataIoWindow>();
        window.ViewModel.Initialize(org, objectApiName, soql);
        // Owner は設定しない（メインウィンドウを前に出せるようにする。中央配置は自前で行う）。
        WindowPlacement.CenterOn(window, owner);
        window.Show();
        return window;
    }
}
