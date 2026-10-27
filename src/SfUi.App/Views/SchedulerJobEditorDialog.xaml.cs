using System.Windows;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>ジョブ編集ダイアログ。</summary>
public partial class SchedulerJobEditorDialog : Window
{
    public SchedulerJobEditorDialog(SchedulerJobEditorViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public SchedulerJobEditorViewModel ViewModel { get; }

    /// <summary>編集結果（キャンセル時は null）。</summary>
    public ScheduledJob? Result { get; private set; }

    /// <summary>保存後にすぐ実行するか。</summary>
    public bool RunAfterSave { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e) => Close(true, runAfterSave: false);

    private void OnSaveAndRun(object sender, RoutedEventArgs e) => Close(true, runAfterSave: true);

    private void Close(bool result, bool runAfterSave)
    {
        var job = ViewModel.TryBuild();
        if (job is null)
        {
            return;
        }

        Result = job;
        RunAfterSave = runAfterSave;
        DialogResult = result;
    }
}
