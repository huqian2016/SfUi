using Avalonia.Controls;
using Avalonia.Interactivity;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Dialogs;

/// <summary>ジョブ編集ダイアログ（Avalonia）。</summary>
public partial class SchedulerJobEditorDialog : Window
{
    public SchedulerJobEditorDialog()
    {
        InitializeComponent();
    }

    public SchedulerJobEditorDialog(SchedulerJobEditorViewModel viewModel)
        : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public SchedulerJobEditorViewModel ViewModel { get; } = null!;

    private void OnSave(object? sender, RoutedEventArgs e) => Close(ViewModel.TryBuild());

    private void OnSaveAndRun(object? sender, RoutedEventArgs e) => Close(ViewModel.TryBuild(), runNow: true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void Close(ScheduledJob? job, bool runNow = false)
    {
        if (job is null)
        {
            return;
        }

        Close(new SchedulerJobEditResult(job, runNow));
    }
}
