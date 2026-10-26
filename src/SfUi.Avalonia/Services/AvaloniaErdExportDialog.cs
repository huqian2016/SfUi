using Avalonia.Controls;
using SfUi.Avalonia.Dialogs;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.Avalonia.Services;

/// <summary>Avalonia の ER 図出力ダイアログ（<see cref="IErdExportDialog"/> 実装）。</summary>
public sealed class AvaloniaErdExportDialog : IErdExportDialog
{
    private readonly TopLevelAccessor _top;
    private readonly IFilePickerService _filePicker;
    private readonly ExportSettingsStore _settings;

    public AvaloniaErdExportDialog(TopLevelAccessor top, IFilePickerService filePicker, ExportSettingsStore settings)
    {
        _top = top;
        _filePicker = filePicker;
        _settings = settings;
    }

    public async Task<ErdExportDialogResult?> ShowAsync(
        string org,
        IReadOnlyList<ErdObjectInfo> objects,
        string suggestedBaseName,
        string? initialDirectory,
        CancellationToken cancellationToken = default)
    {
        var viewModel = new ErdExportDialogViewModel(org, objects, suggestedBaseName, initialDirectory, _filePicker, _settings);
        var window = new ErdExportDialog(viewModel);
        var owner = _top.Current as Window;
        var accepted = owner is not null && await window.ShowDialog<bool>(owner).ConfigureAwait(true);
        return accepted ? viewModel.BuildResult() : null;
    }
}
