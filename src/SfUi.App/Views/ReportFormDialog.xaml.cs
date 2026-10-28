using System.Windows;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>帳票（SVF 風）出力ダイアログ。</summary>
public partial class ReportFormDialog : Window
{
    public ReportFormDialog(ReportFormViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public ReportFormViewModel ViewModel { get; }

    /// <summary>出力結果（キャンセル時は null）。</summary>
    public ReportExportResult? Result { get; private set; }

    private async void OnRender(object sender, RoutedEventArgs e)
    {
        var result = await ViewModel.RenderInternalAsync().ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }
}
