using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>スケジュール ウィンドウ（カレンダー / 実行履歴）。非モーダルで 1 つだけ開く。</summary>
public partial class SchedulerWindow : Window
{
    public SchedulerWindow(SchedulerViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public SchedulerViewModel ViewModel { get; }
}
