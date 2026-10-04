using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>データインポートタブ（CSV 読込 → マッピング → REST / Bulk 実行 → 結果）。</summary>
public sealed partial class DataImportViewModel : ObservableObject, IDisposable
{
    private readonly DataImportService _service;
    private readonly HistoryStore _history;
    private readonly AppSettingsStore _settings;
    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;
    private DataIoViewModel? _owner;
    private CancellationTokenSource? _cts;
    private CsvTable? _csv;

    public DataImportViewModel(
        DataImportService service, HistoryStore history, AppSettingsStore settings,
        IFilePickerService filePicker, IDialogService dialogs, AppLog log)
    {
        _service = service;
        _history = history;
        _settings = settings;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _log = log;
    }

    public ObservableCollection<ImportMappingRowViewModel> Mappings { get; } = new();

    public ObservableCollection<ImportResultRowViewModel> Results { get; } = new();

    public ObservableCollection<DataIoField> ExternalIdFields { get; } = new();

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileInfoText = string.Empty;

    [ObservableProperty]
    private int _encodingIndex;

    [ObservableProperty]
    private int _operationIndex;

    [ObservableProperty]
    private DataIoField? _externalIdField;

    [ObservableProperty]
    private bool _showExternalId;

    [ObservableProperty]
    private bool _emptyAsNull;

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
    private bool _hasResults;

    public bool CanRun => !IsRunning;

    private DataImportOperation Operation => (DataImportOperation)Math.Clamp(OperationIndex, 0, 3);

    private string EncodingPreference => EncodingIndex switch
    {
        1 => CsvParser.EncodingUtf8,
        2 => CsvParser.EncodingShiftJis,
        _ => CsvParser.EncodingAuto,
    };

    public void Attach(DataIoViewModel owner)
    {
        _owner = owner;
        owner.DescribeChanged += OnDescribeChanged;
    }

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CanRun));

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

    partial void OnOperationIndexChanged(int value)
    {
        ShowExternalId = Operation == DataImportOperation.Upsert;
        RebuildMappingRows(remap: true);
    }

    partial void OnEncodingIndexChanged(int value)
    {
        if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
        {
            LoadFile();
        }
    }

    partial void OnExternalIdFieldChanged(DataIoField? value)
    {
        if (Operation == DataImportOperation.Upsert)
        {
            RebuildMappingRows(remap: true);
        }
    }

    private void OnDescribeChanged()
    {
        var describe = _owner?.Describe;

        ExternalIdFields.Clear();
        if (describe is not null)
        {
            foreach (var field in ImportFieldMatcher.ExternalIdFields(describe))
            {
                ExternalIdFields.Add(field);
            }
        }

        if (ExternalIdField is not null &&
            !ExternalIdFields.Any(f => string.Equals(f.Name, ExternalIdField.Name, StringComparison.OrdinalIgnoreCase)))
        {
            ExternalIdField = null;
        }

        if (ExternalIdField is null && ExternalIdFields.Count == 1)
        {
            ExternalIdField = ExternalIdFields[0];
        }

        RebuildMappingRows(remap: true);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var fileName = await _filePicker.OpenFileAsync(UiText.T("DataImport_FileTitle"), UiText.T("DataImport_FileFilter"));
        if (fileName is null)
        {
            return;
        }

        FilePath = fileName;
        LoadFile();
    }

    private void LoadFile()
    {
        try
        {
            var (text, encodingName) = CsvParser.ReadFile(FilePath, EncodingPreference);
            _csv = CsvParser.Parse(text);
            FileInfoText = UiText.T("DataImport_FileInfoFmt", encodingName, _csv.RowCount);
            StatusMessage = _csv.RowCount == 0 ? UiText.T("DataImport_Err_NoRows") : string.Empty;
            Results.Clear();
            HasResults = false;
            SummaryText = string.Empty;
            RebuildMappingRows(remap: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _csv = null;
            FileInfoText = string.Empty;
            StatusMessage = UiText.T("DataImport_Err_ReadFmt", ex.Message);
            _log.Error("CSV ファイルの読み込みに失敗しました", ex);
        }
    }

    private void RebuildMappingRows(bool remap)
    {
        var headers = _csv?.Headers ?? Array.Empty<string>();

        if (headers.Count != Mappings.Count)
        {
            Mappings.Clear();
            var firstRow = _csv is { RowCount: > 0 } ? _csv.Rows[0] : null;
            for (var i = 0; i < headers.Count; i++)
            {
                var sample = firstRow is not null && i < firstRow.Count ? firstRow[i] : string.Empty;
                Mappings.Add(new ImportMappingRowViewModel(i, headers[i], sample));
            }
        }

        var describe = _owner?.Describe;
        var candidates = describe is null
            ? (IReadOnlyList<DataIoField>)Array.Empty<DataIoField>()
            : ImportFieldMatcher.CandidateFields(describe, Operation, ExternalIdField?.Name);
        var suggested = describe is null
            ? null
            : ImportFieldMatcher.Suggest(headers, describe, Operation, ExternalIdField?.Name);

        for (var i = 0; i < Mappings.Count; i++)
        {
            Mappings[i].SetOptions(candidates);

            if (remap && suggested is not null)
            {
                var mapping = suggested[i];
                Mappings[i].SelectedField = mapping.FieldName is null
                    ? null
                    : candidates.FirstOrDefault(c => string.Equals(c.Name, mapping.FieldName, StringComparison.OrdinalIgnoreCase));
                Mappings[i].Include = mapping.IsMapped;
            }
        }
    }

    [RelayCommand]
    private void AutoMap() => RebuildMappingRows(remap: true);

    [RelayCommand]
    private async Task RunAsync()
    {
        if (_owner is null || IsRunning)
        {
            return;
        }

        if (_csv is null || _csv.RowCount == 0)
        {
            StatusMessage = string.IsNullOrEmpty(FilePath)
                ? UiText.T("DataImport_Err_NoFile")
                : UiText.T("DataImport_Err_NoRows");
            return;
        }

        var describe = await _owner.ResolveDescribeAsync();
        if (describe is null)
        {
            return;
        }

        var mappings = Mappings.Select(m => m.ToMapping()).ToList();
        var validation = ImportFieldMatcher.Validate(mappings, _csv.RowCount, Operation, ExternalIdField?.Name);
        if (validation is not null)
        {
            StatusMessage = validation;
            return;
        }

        if (ConfirmPolicies.ShouldConfirm(_settings.Current.ConfirmPolicy, isDangerous: true))
        {
            var body = UiText.T(
                "DataImport_ConfirmBodyFmt",
                describe.Name,
                OperationLabel(Operation),
                _csv.RowCount,
                UseBulk ? UiText.T("DataIo_EngineBulk") : UiText.T("DataIo_EngineRest"));
            var answer = _dialogs.ConfirmDestructive(body, UiText.T("DataImport_ConfirmTitle"));
            if (!answer)
            {
                return;
            }
        }

        var plan = ImportBatchPlanner.BuildPlan(_csv.Rows, mappings, describe, Operation, ExternalIdField?.Name, EmptyAsNull);

        _cts = new CancellationTokenSource();
        IsRunning = true;
        StatusMessage = string.Empty;
        ProgressText = UiText.T("DataImport_RunningFmt", 0, plan.Rows.Count);
        Results.Clear();
        HasResults = false;
        SummaryText = string.Empty;

        try
        {
            var progress = new Progress<ImportProgress>(p => ProgressText = p.Phase == "bulk"
                ? UiText.T("DataImport_BulkRunning")
                : UiText.T("DataImport_RunningFmt", p.Processed, p.Total));

            ImportRunResult result;
            if (UseBulk)
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"sfui-import-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.csv");
                result = await _service.RunBulkAsync(_owner.TargetOrg, describe.Name, Operation, ExternalIdField?.Name, plan, tempPath, progress, _cts.Token);
            }
            else
            {
                result = await _service.RunRestAsync(_owner.TargetOrg, describe.Name, Operation, ExternalIdField?.Name, plan, progress, _cts.Token);
            }

            ApplyResult(result, describe.Name);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = UiText.T("DataExport_Canceled");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("データインポートに失敗しました", ex);
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

    private void ApplyResult(ImportRunResult result, string objectName)
    {
        Results.Clear();
        foreach (var row in result.Rows.OrderBy(r => r.RowIndex >= 0 ? r.RowIndex : int.MaxValue))
        {
            Results.Add(new ImportResultRowViewModel
            {
                RowText = row.RowIndex >= 0 ? (row.RowIndex + 1).ToString() : "?",
                StatusText = row.Success ? UiText.T("DataImport_Ok") : UiText.T("DataImport_Failed"),
                Success = row.Success,
                Id = row.Id,
                Error = row.Error,
            });
        }

        HasResults = true;
        SummaryText = UiText.T(
            "DataImport_SummaryFmt",
            result.SuccessCount,
            result.FailedCount,
            result.SkippedCount,
            result.Duration.TotalSeconds.ToString("0.0"));
        StatusMessage = UiText.T("Msg_DataImportDoneFmt", result.SuccessCount, result.FailedCount);
        AppendHistory(objectName, result);
    }

    private void AppendHistory(string objectName, ImportRunResult result)
    {
        if (_owner is null)
        {
            return;
        }

        try
        {
            _history.Append(new HistoryEntry
            {
                Type = HistoryTypes.Data,
                Org = _owner.TargetOrg,
                Params = $"{Operation}|{objectName}|{Path.GetFileName(FilePath)}",
                Summary = $"{OperationLabel(Operation)} {objectName} — {Path.GetFileName(FilePath)}",
                Status = result.FailedCount == 0 ? "success" : "error",
                DurationMs = (int)result.Duration.TotalMilliseconds,
                ResultInline = UiText.T("DataImport_SummaryFmt", result.SuccessCount, result.FailedCount, result.SkippedCount, result.Duration.TotalSeconds.ToString("0.0")),
            });
        }
        catch (Exception ex)
        {
            _log.Warn($"履歴の保存に失敗しました: {ex.Message}");
        }
    }

    private static string OperationLabel(DataImportOperation operation) => operation switch
    {
        DataImportOperation.Update => UiText.T("DataImport_OpUpdate"),
        DataImportOperation.Upsert => UiText.T("DataImport_OpUpsert"),
        DataImportOperation.Delete => UiText.T("DataImport_OpDelete"),
        _ => UiText.T("DataImport_OpInsert"),
    };

    [RelayCommand]
    private async Task SaveErrorsAsync()
    {
        var failed = Results.Where(r => !r.Success).ToList();
        if (failed.Count == 0)
        {
            StatusMessage = UiText.T("DataImport_Err_NoMapped");
            return;
        }

        var fileName = await _filePicker.SaveFileAsync(
            UiText.T("DataImport_ErrorsTitle"),
            $"import-errors-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            UiText.T("DataImport_ErrorsFilter"));
        if (fileName is null)
        {
            return;
        }

        var table = new DataTable();
        table.Columns.Add(UiText.T("DataImport_ResultRow"), typeof(string));
        table.Columns.Add(UiText.T("DataImport_ResultStatus"), typeof(string));
        table.Columns.Add(UiText.T("DataImport_ResultId"), typeof(string));
        table.Columns.Add(UiText.T("DataImport_ResultError"), typeof(string));
        foreach (var row in failed)
        {
            table.Rows.Add(row.RowText, row.StatusText, row.Id ?? string.Empty, row.Error ?? string.Empty);
        }

        try
        {
            File.WriteAllText(fileName, CsvExporter.ToCsv(table), new UTF8Encoding(true));
            StatusMessage = UiText.T("DataExport_SavedFmt", fileName);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("失敗行の保存に失敗しました", ex);
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
