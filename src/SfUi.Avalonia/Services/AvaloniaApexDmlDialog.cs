using Avalonia.Controls;
using SfUi.App.ViewModels;
using SfUi.Avalonia.Dialogs;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.Avalonia.Services;

/// <summary>DML 文の生成ダイアログ（Avalonia）。</summary>
public sealed class AvaloniaApexDmlDialog : IApexDmlDialog
{
    private readonly SObjectDescribeService _describes;
    private readonly IApexDmlRecordService _records;
    private readonly AppLog _log;
    private readonly TopLevelAccessor _top;

    public AvaloniaApexDmlDialog(SObjectDescribeService describes, IApexDmlRecordService records, AppLog log, TopLevelAccessor top)
    {
        _describes = describes;
        _records = records;
        _log = log;
        _top = top;
    }

    public async Task<string?> GenerateAsync(ApexDmlDialogRequest request)
    {
        var viewModel = new ApexDmlGeneratorViewModel(request.TargetOrg, _describes, _records, _log);
        var dialog = new ApexDmlDialog { DataContext = viewModel };
        dialog.Initialize();
        if (_top.Current is Window owner)
        {
            return await dialog.ShowDialog<string?>(owner);
        }

        dialog.Show();
        return null;
    }
}
