using System.Windows;
using SfUi.App.Views;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.Services;

/// <summary>WPF の ER 図出力ダイアログ（<see cref="IErdExportDialog"/> 実装）。</summary>
public sealed class WpfErdExportDialog : IErdExportDialog
{
    private readonly IFilePickerService _filePicker;

    public WpfErdExportDialog(IFilePickerService filePicker) => _filePicker = filePicker;

    public Task<ErdExportDialogResult?> ShowAsync(
        string org,
        IReadOnlyList<ErdObjectInfo> objects,
        string suggestedBaseName,
        string? initialDirectory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var viewModel = new ErdExportDialogViewModel(org, objects, suggestedBaseName, initialDirectory, _filePicker);
            var window = new ErdExportDialogWindow(viewModel)
            {
                Owner = Application.Current?.MainWindow,
            };

            var accepted = window.ShowDialog() == true;
            return Task.FromResult(accepted ? viewModel.BuildResult() : null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "ERD dialog error");
            return Task.FromResult<ErdExportDialogResult?>(null);
        }
    }
}
