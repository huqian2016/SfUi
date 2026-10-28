using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>ローカル レポート ウィンドウ。</summary>
public partial class ReportsWindow : Window
{
    public ReportsWindow(ReportsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }

    public ReportsViewModel ViewModel { get; }
}
