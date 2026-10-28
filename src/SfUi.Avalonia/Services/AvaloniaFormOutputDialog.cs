using Avalonia.Controls;
using SfUi.App.ViewModels;
using SfUi.Avalonia.Dialogs;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.Avalonia.Services;

/// <summary>IFormOutputDialog の Avalonia 実装（モーダル ダイアログ）。</summary>
public sealed class AvaloniaFormOutputDialog : IFormOutputDialog
{
    private readonly IServiceProvider _services;
    private readonly TopLevelAccessor _top;

    public AvaloniaFormOutputDialog(IServiceProvider services, TopLevelAccessor top)
    {
        _services = services;
        _top = top;
    }

    public async Task<ReportExportResult?> ShowAsync(
        ReportDocument document,
        string suggestedBaseName,
        CancellationToken cancellationToken = default)
    {
        var viewModel = (FormOutputViewModel)_services.GetService(typeof(FormOutputViewModel))!;
        viewModel.Load(document, suggestedBaseName);
        var dialog = new FormOutputDialog(viewModel);

        if (_top.Current is not Window owner || ReferenceEquals(owner, dialog))
        {
            return null;
        }

        return await dialog.ShowDialog<ReportExportResult?>(owner).ConfigureAwait(true);
    }
}
