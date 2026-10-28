using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.ViewModels;
using SfUi.App.Views;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.Services;

/// <summary>IFormOutputDialog の WPF 実装（モーダル ダイアログ）。</summary>
public sealed class WpfFormOutputDialog : IFormOutputDialog
{
    private readonly IServiceProvider _services;

    public WpfFormOutputDialog(IServiceProvider services)
    {
        _services = services;
    }

    public Task<ReportExportResult?> ShowAsync(
        ReportDocument document,
        string suggestedBaseName,
        CancellationToken cancellationToken = default)
    {
        var viewModel = _services.GetRequiredService<FormOutputViewModel>();
        viewModel.Load(document, suggestedBaseName);

        var dialog = new FormOutputDialog(viewModel)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? Application.Current?.MainWindow,
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Result : null);
    }
}
