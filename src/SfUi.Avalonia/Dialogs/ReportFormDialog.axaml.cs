using Avalonia.Controls;
using Avalonia.Interactivity;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Dialogs;

/// <summary>帳票（SVF 風）出力ダイアログ（Avalonia）。</summary>
public partial class ReportFormDialog : Window
{
    public ReportFormDialog()
    {
        InitializeComponent();
    }

    public ReportFormDialog(ReportFormViewModel viewModel)
        : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public ReportFormViewModel ViewModel { get; } = null!;

    /// <summary>出力結果（キャンセル時は null）。</summary>
    public ReportExportResult? Result { get; private set; }

    private async void OnRender(object? sender, RoutedEventArgs e)
    {
        var result = await ViewModel.RenderInternalAsync().ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        Result = result;
        Close(result);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
