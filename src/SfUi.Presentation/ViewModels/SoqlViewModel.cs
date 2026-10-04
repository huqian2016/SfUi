using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>SOQL タブの ViewModel。</summary>
public partial class SoqlViewModel : ObservableObject
{
    private readonly SoqlService _service;
    private readonly HistoryStore _history;
    private readonly FavoritesStore _favorites;
    private readonly IFilePickerService _filePicker;
    private readonly IClipboardService _clipboard;
    private readonly AppLog _log;

    [ObservableProperty]
    private string _soqlText = "SELECT Id, Name FROM Account LIMIT 10";

    [ObservableProperty]
    private bool _preferRest = true;

    [ObservableProperty]
    private bool _useToolingApi;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = UiText.T("Common_Ready");

    [ObservableProperty]
    private DataView? _resultView;

    [ObservableProperty]
    private HistoryEntry? _selectedHistoryItem;

    /// <summary>現在の対象組織（MainViewModel から設定される）。</summary>
    public string? CurrentOrg { get; set; }

    /// <summary>現在の SF 実行フォルダ（MainViewModel から設定される）。</summary>
    public string? CurrentFolder { get; set; }

    public ObservableCollection<HistoryEntry> HistoryItems { get; } = new();

    public SoqlViewModel(SoqlService service, HistoryStore history, FavoritesStore favorites, IFilePickerService filePicker, IClipboardService clipboard, AppLog log)
    {
        _service = service;
        _history = history;
        _favorites = favorites;
        _filePicker = filePicker;
        _clipboard = clipboard;
        _log = log;
        RefreshHistory();
    }

    [RelayCommand]
    private void RefreshHistory()
    {
        HistoryItems.Clear();
        foreach (var entry in _history.Query(new HistoryQuery(Type: HistoryTypes.Soql)).Take(30))
        {
            HistoryItems.Add(entry);
        }
    }

    partial void OnSelectedHistoryItemChanged(HistoryEntry? value)
    {
        if (value?.Params is { Length: > 0 } text)
        {
            SoqlText = text;
        }
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var soql = SoqlText.Trim();
        if (string.IsNullOrWhiteSpace(CurrentOrg))
        {
            StatusText = UiText.T("Msg_SelectOrg");
            return;
        }

        if (string.IsNullOrEmpty(soql))
        {
            StatusText = UiText.T("Soql_EnterQuery");
            return;
        }

        IsRunning = true;
        StatusText = UiText.T("Common_Running");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var execution = await _service.ExecuteSoqlAsync(CurrentOrg, soql, UseToolingApi, PreferRest, CurrentFolder);
            ResultView = execution.Result.Table.DefaultView;
            StatusText = UiText.T("Soql_ResultFmt", execution.Engine, execution.Result.TotalSize, execution.Duration.TotalMilliseconds);

            _history.Append(
                new HistoryEntry
                {
                    Type = HistoryTypes.Soql,
                    Org = CurrentOrg,
                    Folder = CurrentFolder,
                    Params = soql,
                    Summary = Summarize(soql),
                    Status = "success",
                    DurationMs = (int)execution.Duration.TotalMilliseconds,
                },
                result: execution.Result.RawJson);

            _log.Info($"SOQL 実行: {StatusText}");
        }
        catch (Exception ex)
        {
            ResultView = null;
            StatusText = UiText.T("Common_FailedFmt", ex.Message);
            _history.Append(
                new HistoryEntry
                {
                    Type = HistoryTypes.Soql,
                    Org = CurrentOrg,
                    Folder = CurrentFolder,
                    Params = soql,
                    Summary = Summarize(soql),
                    Status = "error",
                    DurationMs = (int)stopwatch.Elapsed.TotalMilliseconds,
                },
                result: $"ERROR: {ex.Message}");
            _log.Error("SOQL 実行に失敗", ex);
        }
        finally
        {
            IsRunning = false;
            RefreshHistory();
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var table = ResultView?.Table;
        if (table is null || table.Rows.Count == 0)
        {
            StatusText = UiText.T("Soql_NoResultsToExport");
            return;
        }

        var initialDirectory = !string.IsNullOrWhiteSpace(CurrentFolder) && Directory.Exists(CurrentFolder) ? CurrentFolder : null;
        var fileName = await _filePicker.SaveFileAsync(
            UiText.T("Soql_CsvSaveTitle"),
            $"soql-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            UiText.T("Soql_CsvFilter"),
            initialDirectory);
        if (fileName is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(fileName, CsvExporter.ToCsv(table), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusText = UiText.T("Soql_CsvSavedFmt", fileName);
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Soql_CsvFailedFmt", ex.Message);
            _log.Error("CSV 出力に失敗", ex);
        }
    }

    [RelayCommand]
    private void CopyResults()
    {
        var table = ResultView?.Table;
        if (table is null || table.Rows.Count == 0)
        {
            StatusText = UiText.T("Soql_NoResultsToCopy");
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine(string.Join("\t", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow row in table.Rows)
        {
            builder.AppendLine(string.Join("\t", table.Columns.Cast<DataColumn>().Select(c => row[c] is DBNull ? "" : row[c]?.ToString())));
        }

        if (_clipboard.TrySetText(builder.ToString()))
        {
            StatusText = UiText.T("Soql_Copied");
        }
        else
        {
            StatusText = UiText.T("Msg_CopyFailedFmt", "Clipboard not available");
        }
    }

    [RelayCommand]
    private void AddFavorite()
    {
        var soql = SoqlText.Trim();
        if (string.IsNullOrEmpty(soql))
        {
            StatusText = UiText.T("Soql_NoQueryFavorite");
            return;
        }

        var label = Summarize(soql);
        _favorites.Add(HistoryTypes.Soql, label, soql);
        StatusText = UiText.T("Common_FavoriteAddedFmt", label);
    }

    /// <summary>履歴から読み込む（autoRun=true ならそのまま実行）。</summary>
    public void LoadFromHistory(HistoryEntry entry, bool autoRun)
    {
        if (entry.Params is { Length: > 0 } text)
        {
            SoqlText = text;
        }

        if (autoRun)
        {
            _ = ExecuteAsync();
        }
    }

    /// <summary>外部（AI チャット等）から本文を読み込む。</summary>
    public void LoadText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            SoqlText = text;
        }
    }

    /// <summary>お気に入りから読み込む（autoRun=true ならそのまま実行）。</summary>
    public void LoadFavorite(FavoriteItem item, bool autoRun)
    {
        if (item.Payload is { Length: > 0 } text)
        {
            SoqlText = text;
        }

        if (autoRun)
        {
            _ = ExecuteAsync();
        }
    }

    private static string Summarize(string text)
    {
        var single = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= 80 ? single : single[..80] + "…";
    }
}
