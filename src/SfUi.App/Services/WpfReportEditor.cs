using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.ViewModels;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>IReportEditorDialog の WPF 実装（モーダル ダイアログ）。</summary>
public sealed class WpfReportEditor : IReportEditorDialog
{
    private readonly IServiceProvider _services;

    public WpfReportEditor(IServiceProvider services)
    {
        _services = services;
    }

    public Task<ReportDefinition?> EditAsync(
        ReportDefinition? definition,
        string? targetOrg,
        CancellationToken cancellationToken = default)
    {
        var viewModel = _services.GetRequiredService<ReportEditorViewModel>();

        // ダイアログはすぐ開き、オブジェクト / 項目の読み込みはバックグラウンドで進める
        viewModel.Load(definition, targetOrg);

        var dialog = new ReportEditorDialog(viewModel)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? Application.Current?.MainWindow,
        };

        _ = viewModel.LoadObjectsAndFieldsAsync(cancellationToken);

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Result : null);
    }
}
