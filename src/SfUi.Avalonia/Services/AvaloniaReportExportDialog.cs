using Avalonia.Controls;
using SfUi.Avalonia.Dialogs;
using SfUi.App.ViewModels;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.Avalonia.Services;

/// <summary>Avalonia のレポート出力ダイアログ（<see cref="IReportExportDialog"/> 実装）。</summary>
public sealed class AvaloniaReportExportDialog : IReportExportDialog
{
    private readonly TopLevelAccessor _top;
    private readonly IFilePickerService _filePicker;
    private readonly ExportSettingsStore _settings;

    public AvaloniaReportExportDialog(TopLevelAccessor top, IFilePickerService filePicker, ExportSettingsStore settings)
    {
        _top = top;
        _filePicker = filePicker;
        _settings = settings;
    }

    public async Task<ReportExportOptions?> ShowAsync(
        ReportDocument document,
        string suggestedBaseName,
        string? initialDirectory,
        CancellationToken cancellationToken = default)
    {
        var viewModel = new ReportExportDialogViewModel(document, suggestedBaseName, initialDirectory, _filePicker, _settings);
        var window = new ReportExportDialog(viewModel);
        var owner = _top.Current as Window;
        var accepted = owner is null ? false : await window.ShowDialog<bool>(owner).ConfigureAwait(true);
        return accepted ? viewModel.BuildOptions() : null;
    }
}
