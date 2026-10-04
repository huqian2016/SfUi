using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>データエクスポートタブ（ビルダー / SOQL 直接入力 + REST / Bulk 実行 + 保存）。</summary>
public sealed partial class DataExportViewModel : ObservableObject, IDisposable
{
    private readonly DataExportService _service;
    private readonly HistoryStore _history;
    private readonly IFilePickerService _filePicker;
    private readonly AppLog _log;
    private readonly List<DataIoFieldItemViewModel> _allFields = new();
    private DataIoViewModel? _owner;
    private CancellationTokenSource? _cts;
    private string? _rawJson;

    public DataExportViewModel(DataExportService service, HistoryStore history, IFilePickerService filePicker, AppLog log)
    {
        _service = service;
        _history = history;
        _filePicker = filePicker;
        _log = log;
    }

    public ObservableCollection<DataIoFieldItemViewModel> Fields { get; } = new();

    [ObservableProperty]
    private bool _builderMode = true;

    [ObservableProperty]
    private bool _soqlMode;

    [ObservableProperty]
    private string _fieldFilter = string.Empty;

    [ObservableProperty]
    private string _whereText = string.Empty;

    [ObservableProperty]
    private string _orderByText = string.Empty;

    [ObservableProperty]
    private string _limitText = string.Empty;

    [ObservableProperty]
    private string _soqlText = string.Empty;

    [ObservableProperty]
    private bool _useRest = true;

    [ObservableProperty]
    private bool _useBulk;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private DataView? _resultView;

    [ObservableProperty]
    private bool _hasResult;

    public bool CanRun => !IsRunning;

    public void Attach(DataIoViewModel owner)
    {
        _owner = owner;
        owner.DescribeChanged += OnDescribeChanged;
    }

    /// <summary>履歴からの再実行用に SOQL を復元する。</summary>
    public void PresetSoql(string soql)
    {
        SoqlText = soql;
        BuilderMode = false;
        SoqlMode = true;
    }

    partial void OnBuilderModeChanged(bool value)
    {
        if (value == SoqlMode)
        {
            SoqlMode = !value;
        }

        if (value)
        {
            RegenerateSoql();
        }
    }

    partial void OnSoqlModeChanged(bool value)
    {
        if (value == BuilderMode)
        {
            BuilderMode = !value;
        }
    }

    partial void OnUseRestChanged(bool value)
    {
        if (value == UseBulk)
        {
            UseBulk = !value;
        }
    }

    partial void OnUseBulkChanged(bool value)
    {
        if (value == UseRest)
        {
            UseRest = !value;
        }
    }

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CanRun));

    partial void OnFieldFilterChanged(string value) => ApplyFieldFilter();

    partial void OnWhereTextChanged(string value) => RegenerateSoql();

    partial void OnOrderByTextChanged(string value) => RegenerateSoql();

    partial void OnLimitTextChanged(string value) => RegenerateSoql();

    private void OnDescribeChanged()
    {
        var describe = _owner?.Describe;
        var previous = _allFields.ToDictionary(f => f.Field.Name, f => f.IsSelected, StringComparer.OrdinalIgnoreCase);

        _allFields.Clear();
        if (describe is not null)
        {
            foreach (var field in describe.Fields)
            {
                var item = new DataIoFieldItemViewModel(field);
                if (previous.TryGetValue(field.Name, out var selected))
                {
                    item.IsSelected = selected;
                }
                else
                {
                    // 既定は Id と Name（一般的な確認用）
                    item.IsSelected = field.Name is "Id" or "Name";
                }

                item.PropertyChanged += (_, _) => RegenerateSoql();
                _allFields.Add(item);
            }
        }

        ApplyFieldFilter();
        RegenerateSoql();
    }

    private void ApplyFieldFilter()
    {
        Fields.Clear();
        var filter = FieldFilter?.Trim() ?? string.Empty;
        foreach (var item in _allFields)
        {
            if (filter.Length == 0 || item.Display.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                Fields.Add(item);
            }
        }
    }

    private void RegenerateSoql()
    {
        if (!BuilderMode || _owner?.Describe is null)
        {
            return;
        }

        try
        {
            var fields = _allFields.Where(f => f.IsSelected).Select(f => f.Field.Name).ToList();
            SoqlText = fields.Count == 0
                ? string.Empty
                : DataIoQueryBuilder.Build(_owner.Describe.Name, fields, WhereText, OrderByText, ParseLimit());
        }
        catch (ArgumentException)
        {
            SoqlText = string.Empty;
        }
    }

    private int? ParseLimit() => int.TryParse(LimitText, out var value) && value > 0 ? value : null;

    [RelayCommand]
    private void SelectAllFields()
    {
        foreach (var field in _allFields)
        {
            field.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearFields()
    {
        foreach (var field in _allFields)
        {
            field.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (_owner is null || IsRunning)
        {
            return;
        }

        var soql = SoqlText?.Trim() ?? string.Empty;

        if (BuilderMode)
        {
            var describe = await _owner.ResolveDescribeAsync();
            if (describe is null)
            {
                return;
            }

            RegenerateSoql();
            soql = SoqlText?.Trim() ?? string.Empty;
        }

        if (soql.Length == 0)
        {
            StatusMessage = BuilderMode ? UiText.T("DataIo_Err_FieldsRequired") : UiText.T("DataIo_Err_SoqlRequired");
            return;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        StatusMessage = string.Empty;
        ProgressText = string.Empty;

        try
        {
            if (UseBulk)
            {
                var fileName = await _filePicker.SaveFileAsync(
                    UiText.T("DataExport_CsvTitle"),
                    $"export-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
                    UiText.T("DataExport_CsvFilter") + "|" + UiText.T("DataExport_JsonFilter"));
                if (fileName is null)
                {
                    return;
                }

                var format = string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase) ? "json" : "csv";
                ProgressText = UiText.T("DataExport_BulkRunning");
                var progress = new Progress<DataExportProgress>(_ => ProgressText = UiText.T("DataExport_BulkRunning"));
                var result = await _service.RunBulkAsync(_owner.TargetOrg, soql, fileName, format, progress, _cts.Token);
                ApplyResult(result, soql, fileName, "Bulk");
            }
            else
            {
                var progress = new Progress<DataExportProgress>(p => ProgressText = UiText.T("DataExport_RunningFmt", p.Rows, p.Pages));
                var result = await _service.RunRestAsync(_owner.TargetOrg, soql, progress, _cts.Token);
                ApplyResult(result, soql, null, "REST");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = UiText.T("DataExport_Canceled");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("データエクスポートに失敗しました", ex);
            AppendHistory(soql, "error", ex.Message);
        }
        finally
        {
            IsRunning = false;
            ProgressText = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private void ApplyResult(DataExportResult result, string soql, string? bulkFile, string engine)
    {
        ResultView = result.Result.Table.DefaultView;
        _rawJson = result.Result.RawJson;
        HasResult = result.Result.RowCount > 0;
        SummaryText = result.Result.Done
            ? UiText.T("DataExport_SummaryDoneFmt", result.Result.RowCount)
            : UiText.T("DataExport_SummaryFmt", result.Result.RowCount, result.Result.TotalSize);
        StatusMessage = bulkFile is not null
            ? UiText.T("DataExport_BulkFileFmt", result.Result.RowCount, bulkFile)
            : UiText.T("Msg_DataExportDoneFmt", result.Result.RowCount, engine);
        AppendHistory(soql, "success", $"{result.Result.RowCount}");
    }

    private void AppendHistory(string soql, string status, string resultText)
    {
        if (_owner is null)
        {
            return;
        }

        try
        {
            var summary = soql.Length > 120 ? soql[..120] + "…" : soql;
            _history.Append(new HistoryEntry
            {
                Type = HistoryTypes.Data,
                Org = _owner.TargetOrg,
                Params = soql,
                Summary = summary,
                Status = status,
                ResultInline = resultText,
            });
        }
        catch (Exception ex)
        {
            _log.Warn($"履歴の保存に失敗しました: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveCsvAsync()
    {
        if (ResultView?.Table is not { } table)
        {
            StatusMessage = UiText.T("DataExport_NoResult");
            return;
        }

        await SaveTextAsync(CsvExporter.ToCsv(table), UiText.T("DataExport_CsvTitle"), UiText.T("DataExport_CsvFilter"), "csv", bom: true);
    }

    [RelayCommand]
    private async Task SaveJsonAsync()
    {
        if (_rawJson is null)
        {
            StatusMessage = UiText.T("DataExport_NoResult");
            return;
        }

        await SaveTextAsync(_rawJson, UiText.T("DataExport_JsonTitle"), UiText.T("DataExport_JsonFilter"), "json", bom: false);
    }

    [RelayCommand]
    private async Task SaveTsvAsync()
    {
        if (ResultView?.Table is not { } table)
        {
            StatusMessage = UiText.T("DataExport_NoResult");
            return;
        }

        await SaveTextAsync(CsvExporter.ToTsv(table), UiText.T("DataExport_TsvTitle"), UiText.T("DataExport_TsvFilter"), "tsv", bom: true);
    }

    private async Task SaveTextAsync(string content, string title, string filter, string extension, bool bom)
    {
        var fileName = await _filePicker.SaveFileAsync(title, $"export-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}", filter);
        if (fileName is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(fileName, content, new UTF8Encoding(bom));
            StatusMessage = UiText.T("DataExport_SavedFmt", fileName);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("エクスポート結果の保存に失敗しました", ex);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        if (_owner is not null)
        {
            _owner.DescribeChanged -= OnDescribeChanged;
        }
    }
}
