using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.ViewModels;
using SfUi.App.Views;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.Services;

/// <summary>IReportFormDialog の WPF 実装（モーダル ダイアログ）。</summary>
public sealed class WpfReportFormDialog : IReportFormDialog
{
    private readonly IServiceProvider _services;

    public WpfReportFormDialog(IServiceProvider services)
    {
        _services = services;
    }

    public Task<ReportExportResult?> ShowAsync(
        ReportDocument document,
        string suggestedBaseName,
        CancellationToken cancellationToken = default)
    {
        var viewModel = _services.GetRequiredService<ReportFormViewModel>();
        viewModel.Load(document, suggestedBaseName);

        var dialog = new ReportFormDialog(viewModel)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? Application.Current?.MainWindow,
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Result : null);
    }
}
