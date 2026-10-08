using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>項目の使用箇所（フィールド影響分析）ウィンドウを新しい非モーダル ウィンドウとして開く。</summary>
public sealed class FieldUsageWindowFactory
{
    private readonly IServiceProvider _services;

    public FieldUsageWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    public FieldUsageWindow Open(OrgInfo org, string objectApiName, string fieldApiName, string? fieldLabel, Window? owner = null)
    {
        var window = _services.GetRequiredService<FieldUsageWindow>();
        window.ViewModel.Initialize(org, objectApiName, fieldApiName, fieldLabel);
        // Owner は設定しない（メインウィンドウを前に出せるようにする。中央配置は自前で行う）。
        WindowPlacement.CenterOn(window, owner);
        window.Show();
        return window;
    }
}
