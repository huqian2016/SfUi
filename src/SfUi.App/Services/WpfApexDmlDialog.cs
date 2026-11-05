using System.Windows;
using SfUi.App.ViewModels;
using SfUi.App.Views;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.Services;

/// <summary>DML 文の生成ダイアログ（WPF）。</summary>
public sealed class WpfApexDmlDialog : IApexDmlDialog
{
    private readonly SObjectDescribeService _describes;
    private readonly IApexDmlRecordService _records;
    private readonly AppLog _log;

    public WpfApexDmlDialog(SObjectDescribeService describes, IApexDmlRecordService records, AppLog log)
    {
        _describes = describes;
        _records = records;
        _log = log;
    }

    public Task<string?> GenerateAsync(ApexDmlDialogRequest request)
    {
        var viewModel = new ApexDmlGeneratorViewModel(request.TargetOrg, _describes, _records, _log);
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
        var dialog = new ApexDmlDialog
        {
            DataContext = viewModel,
            Owner = owner,
        };

        dialog.Initialize();
        var ok = dialog.ShowDialog() == true;
        return Task.FromResult(ok ? dialog.Result : null);
    }
}
