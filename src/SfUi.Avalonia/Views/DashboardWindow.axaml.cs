using Avalonia.Controls;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>レポートのダッシュボード（グラフを並べて定期更新する）ウィンドウ（Avalonia）。</summary>
public partial class DashboardWindow : Window
{
    public DashboardWindow()
    {
        InitializeComponent();
        Closed += (_, _) => ViewModel?.Dispose();
    }

    public DashboardWindow(DashboardViewModel viewModel)
        : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public DashboardViewModel? ViewModel { get; }
}
