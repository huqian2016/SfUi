using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>デバッグログ解析ウィンドウ（非モーダル・複数同時表示可）。</summary>
public partial class LogAnalyzerWindow : Window
{
    public LogAnalyzerWindow(LogAnalyzerViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public LogAnalyzerViewModel ViewModel { get; }
}
