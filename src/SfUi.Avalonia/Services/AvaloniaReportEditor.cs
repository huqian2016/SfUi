using Avalonia.Controls;
using SfUi.App.ViewModels;
using SfUi.Avalonia.Dialogs;
using SfUi.Core;

namespace SfUi.Avalonia.Services;

/// <summary>IReportEditorDialog の Avalonia 実装（モーダル ダイアログ）。</summary>
public sealed class AvaloniaReportEditor : IReportEditorDialog
{
    private readonly IServiceProvider _services;
    private readonly TopLevelAccessor _top;

    public AvaloniaReportEditor(IServiceProvider services, TopLevelAccessor top)
    {
        _services = services;
        _top = top;
    }

    public async Task<ReportDefinition?> EditAsync(
        ReportDefinition? definition,
        string? targetOrg,
        CancellationToken cancellationToken = default)
    {
        var viewModel = (ReportEditorViewModel)_services.GetService(typeof(ReportEditorViewModel))!;

        // ダイアログはすぐ開き、オブジェクト / 項目の読み込みはバックグラウンドで進める
        viewModel.Load(definition, targetOrg);
        var dialog = new ReportEditorDialog(viewModel);

        if (_top.Current is not Window owner || ReferenceEquals(owner, dialog))
        {
            return null;
        }

        _ = viewModel.LoadObjectsAndFieldsAsync(cancellationToken);
        return await dialog.ShowDialog<ReportDefinition?>(owner).ConfigureAwait(true);
    }
}
