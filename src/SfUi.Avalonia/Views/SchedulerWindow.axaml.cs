using Avalonia.Controls;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>スケジュール ウィンドウ（カレンダー / 実行履歴）。</summary>
public partial class SchedulerWindow : Window
{
    public SchedulerWindow()
    {
        InitializeComponent();
    }

    public SchedulerWindow(SchedulerViewModel viewModel)
        : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public SchedulerViewModel ViewModel { get; } = null!;
}
