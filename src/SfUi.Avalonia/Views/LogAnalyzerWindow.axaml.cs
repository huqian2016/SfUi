using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>デバッグログ解析ウィンドウ（非モーダル・複数同時表示可）。</summary>
public partial class LogAnalyzerWindow : Window
{
    private readonly LogAnalyzerViewModel _viewModel = null!;

    /// <summary>XAML ローダー用（実際の生成はコンストラクター経由）。</summary>
    public LogAnalyzerWindow() => AvaloniaXamlLoader.Load(this);

    public LogAnalyzerWindow(LogAnalyzerViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public LogAnalyzerViewModel ViewModel => _viewModel;
}
