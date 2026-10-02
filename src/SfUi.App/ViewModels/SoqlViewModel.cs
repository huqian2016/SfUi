using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>SOQL タブの ViewModel。</summary>
public partial class SoqlViewModel : ObservableObject
{
    private readonly SoqlService _service;
    private readonly HistoryStore _history;
    private readonly FavoritesStore _favorites;
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
    private string _statusText = "準備完了";

    [ObservableProperty]
    private DataView? _resultView;

    [ObservableProperty]
    private HistoryEntry? _selectedHistoryItem;

    /// <summary>現在の対象組織（MainViewModel から設定される）。</summary>
    public string? CurrentOrg { get; set; }

    /// <summary>現在の SF 実行フォルダ（MainViewModel から設定される）。</summary>
    public string? CurrentFolder { get; set; }

    public ObservableCollection<HistoryEntry> HistoryItems { get; } = new();

    public SoqlViewModel(SoqlService service, HistoryStore history, FavoritesStore favorites, AppLog log)
    {
        _service = service;
        _history = history;
        _favorites = favorites;
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
            StatusText = "上部バーで組織を選択してください";
            return;
        }

        if (string.IsNullOrEmpty(soql))
        {
            StatusText = "SOQL を入力してください";
            return;
        }

        IsRunning = true;
        StatusText = "実行中…";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var execution = await _service.ExecuteSoqlAsync(CurrentOrg, soql, UseToolingApi, PreferRest, CurrentFolder);
            ResultView = execution.Result.Table.DefaultView;
            StatusText = $"{execution.Engine} / {execution.Result.TotalSize} 件 / {execution.Duration.TotalMilliseconds:F0} ms";

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
            StatusText = $"失敗: {ex.Message}";
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
    private void ExportCsv()
    {
        var table = ResultView?.Table;
        if (table is null || table.Rows.Count == 0)
        {
            StatusText = "出力できる結果がありません";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "SOQL 結果を CSV 保存",
            FileName = $"soql-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            Filter = "CSV ファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
        };
        if (!string.IsNullOrWhiteSpace(CurrentFolder) && Directory.Exists(CurrentFolder))
        {
            dialog.InitialDirectory = CurrentFolder;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, CsvExporter.ToCsv(table), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusText = $"CSV 出力: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"CSV 出力に失敗: {ex.Message}";
            _log.Error("CSV 出力に失敗", ex);
        }
    }

    [RelayCommand]
    private void CopyResults()
    {
        var table = ResultView?.Table;
        if (table is null || table.Rows.Count == 0)
        {
            StatusText = "コピーできる結果がありません";
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine(string.Join("\t", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow row in table.Rows)
        {
            builder.AppendLine(string.Join("\t", table.Columns.Cast<DataColumn>().Select(c => row[c] is DBNull ? "" : row[c]?.ToString())));
        }

        try
        {
            Clipboard.SetText(builder.ToString());
            StatusText = "結果をクリップボードにコピーしました（TSV）";
        }
        catch (Exception ex)
        {
            StatusText = $"コピーに失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddFavorite()
    {
        var soql = SoqlText.Trim();
        if (string.IsNullOrEmpty(soql))
        {
            StatusText = "お気に入りに追加する SOQL がありません";
            return;
        }

        var label = Summarize(soql);
        _favorites.Add(HistoryTypes.Soql, label, soql);
        StatusText = $"お気に入りに追加: {label}";
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
