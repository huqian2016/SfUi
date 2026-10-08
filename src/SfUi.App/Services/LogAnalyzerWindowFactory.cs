using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>デバッグログ解析ウィンドウを新しい非モーダル ウィンドウとして開く（複数同時表示対応）。</summary>
public sealed class LogAnalyzerWindowFactory
{
    private readonly IServiceProvider _services;

    public LogAnalyzerWindowFactory(IServiceProvider services)
    {
        _services = services;
    }

    public LogAnalyzerWindow Open(DebugLogAnalysis analysis, string orgLabel, string sourceLabel, Window? owner = null)
    {
        var window = _services.GetRequiredService<LogAnalyzerWindow>();
        window.ViewModel.Initialize(analysis, orgLabel, sourceLabel);
        // Owner は設定しない（メインウィンドウを前に出せるようにする。中央配置は自前で行う）。
        WindowPlacement.CenterOn(window, owner);
        window.Show();
        return window;
    }
}
