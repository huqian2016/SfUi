using System.Windows;
using SfUi.App.ViewModels;
using SfUi.App.Views;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.Services;

/// <summary>WPF のレポート出力ダイアログ（<see cref="IReportExportDialog"/> 実装）。</summary>
public sealed class WpfReportExportDialog : IReportExportDialog
{
    private readonly IFilePickerService _filePicker;

    public WpfReportExportDialog(IFilePickerService filePicker) => _filePicker = filePicker;

    public Task<ReportExportOptions?> ShowAsync(
        ReportDocument document,
        string suggestedBaseName,
        string? initialDirectory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var viewModel = new ReportExportDialogViewModel(document, suggestedBaseName, initialDirectory, _filePicker);
            var window = new ReportExportDialogWindow(viewModel)
            {
                Owner = Application.Current?.MainWindow,
            };

            var accepted = window.ShowDialog() == true;
            return Task.FromResult(accepted ? viewModel.BuildOptions() : null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Export dialog error");
            return Task.FromResult<ReportExportOptions?>(null);
        }
    }
}
