using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>レポートのダッシュボード（グラフを並べて定期更新する）ウィンドウ。</summary>
public partial class DashboardWindow : Window
{
    public DashboardWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }

    public DashboardViewModel ViewModel { get; }
}
