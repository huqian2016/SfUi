using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>匿名Apex タブの ViewModel。</summary>
public partial class ApexViewModel : ObservableObject
{
    private readonly ApexService _service;
    private readonly HistoryStore _history;
    private readonly FavoritesStore _favorites;
    private readonly AppLog _log;

    [ObservableProperty]
    private string _apexCode = "System.debug('Hello SfUi');";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "準備完了";

    [ObservableProperty]
    private string? _resultSummary;

    [ObservableProperty]
    private string? _resultLogs;

    [ObservableProperty]
    private HistoryEntry? _selectedHistoryItem;

    /// <summary>現在の対象組織（MainViewModel から設定される）。</summary>
    public string? CurrentOrg { get; set; }

    /// <summary>現在の SF 実行フォルダ（MainViewModel から設定される）。</summary>
    public string? CurrentFolder { get; set; }

    public ObservableCollection<HistoryEntry> HistoryItems { get; } = new();

    public ApexViewModel(ApexService service, HistoryStore history, FavoritesStore favorites, AppLog log)
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
        foreach (var entry in _history.Query(new HistoryQuery(Type: HistoryTypes.Apex)).Take(30))
        {
            HistoryItems.Add(entry);
        }
    }

    partial void OnSelectedHistoryItemChanged(HistoryEntry? value)
    {
        if (value?.Params is { Length: > 0 } text)
        {
            ApexCode = text;
        }
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var code = ApexCode;
        if (string.IsNullOrWhiteSpace(CurrentOrg))
        {
            StatusText = "上部バーで組織を選択してください";
            return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            StatusText = "Apex コードを入力してください";
            return;
        }

        IsRunning = true;
        StatusText = "実行中…";
        ResultSummary = null;
        ResultLogs = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _service.ExecuteAnonymousAsync(CurrentOrg, code, CurrentFolder);

            if (result.CompileProblem is { Length: > 0 } compileProblem)
            {
                StatusText = "コンパイルエラー";
                ResultSummary = $"コンパイルエラー: {compileProblem}";
                AppendHistory(code, "error", (int)result.Duration.TotalMilliseconds, ResultSummary);
            }
            else if (result.ExceptionMessage is { Length: > 0 } exceptionMessage)
            {
                StatusText = "実行時例外";
                ResultSummary = $"例外 (line {result.Line}, column {result.Column}): {exceptionMessage}{Environment.NewLine}{result.ExceptionStackTrace}";
                ResultLogs = result.Logs;
                AppendHistory(code, "error", (int)result.Duration.TotalMilliseconds, ResultSummary, "log", result.Logs);
            }
            else if (result.Success && result.Compiled)
            {
                StatusText = $"成功 / {result.Duration.TotalMilliseconds:F0} ms";
                ResultSummary = result.Logs is { Length: > 0 } ? "実行成功（デバッグログを表示中）" : "実行成功";
                ResultLogs = result.Logs;
                AppendHistory(code, "success", (int)result.Duration.TotalMilliseconds, ResultSummary, "log", result.Logs);
            }
            else
            {
                var message = result.ErrorMessage ?? "(詳細不明)";
                StatusText = $"失敗: {message}";
                ResultSummary = message;
                AppendHistory(code, "error", (int)result.Duration.TotalMilliseconds, ResultSummary);
            }

            _log.Info($"匿名Apex 実行: {StatusText}");
        }
        catch (Exception ex)
        {
            StatusText = $"失敗: {ex.Message}";
            ResultSummary = ex.Message;
            AppendHistory(code, "error", (int)stopwatch.Elapsed.TotalMilliseconds, ResultSummary);
            _log.Error("匿名Apex 実行に失敗", ex);
        }
        finally
        {
            IsRunning = false;
            RefreshHistory();
        }
    }

    [RelayCommand]
    private async Task FetchLatestLogAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentOrg))
        {
            StatusText = "上部バーで組織を選択してください";
            return;
        }

        try
        {
            var logText = await _service.GetLogAsync(CurrentOrg, latestCount: 1);
            if (string.IsNullOrEmpty(logText))
            {
                StatusText = "org に保存されたデバッグログがありません（Setup の Debug Logs / sf apex tail log で作成できます）";
                return;
            }

            ResultLogs = logText;
            StatusText = $"最新ログを取得しました（{logText.Length:N0} 文字）";
        }
        catch (Exception ex)
        {
            StatusText = $"ログ取得に失敗: {ex.Message}";
            _log.Error("最新ログの取得に失敗", ex);
        }
    }

    [RelayCommand]
    private void LoadFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "匿名Apex ファイルを開く",
            Filter = "Apex ファイル (*.apex)|*.apex|テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
        };
        var scriptsDirectory = GetScriptsDirectory();
        if (scriptsDirectory is not null)
        {
            dialog.InitialDirectory = scriptsDirectory;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ApexCode = File.ReadAllText(dialog.FileName);
            StatusText = $"読み込み: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"読み込みに失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveFile()
    {
        if (string.IsNullOrWhiteSpace(ApexCode))
        {
            StatusText = "保存するコードがありません";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "匿名Apex を保存",
            FileName = $"anonymous-{DateTime.Now:yyyyMMdd-HHmmss}.apex",
            Filter = "Apex ファイル (*.apex)|*.apex|すべてのファイル (*.*)|*.*",
        };
        var scriptsDirectory = GetScriptsDirectory();
        if (scriptsDirectory is not null)
        {
            dialog.InitialDirectory = scriptsDirectory;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, ApexCode, new UTF8Encoding(false));
            StatusText = $"保存: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"保存に失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddFavorite()
    {
        if (string.IsNullOrWhiteSpace(ApexCode))
        {
            StatusText = "お気に入りに追加するコードがありません";
            return;
        }

        var label = Summarize(ApexCode);
        _favorites.Add(HistoryTypes.Apex, label, ApexCode);
        StatusText = $"お気に入りに追加: {label}";
    }

    /// <summary>履歴から読み込む（autoRun=true ならそのまま実行）。</summary>
    public void LoadFromHistory(HistoryEntry entry, bool autoRun)
    {
        if (entry.Params is { Length: > 0 } text)
        {
            ApexCode = text;
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
            ApexCode = text;
        }

        if (autoRun)
        {
            _ = ExecuteAsync();
        }
    }

    private void AppendHistory(string code, string status, int durationMs, string? summary, string resultExtension = "json", string? result = null)
    {
        _history.Append(
            new HistoryEntry
            {
                Type = HistoryTypes.Apex,
                Org = CurrentOrg,
                Folder = CurrentFolder,
                Params = code,
                Summary = Summarize(code),
                Status = status,
                DurationMs = durationMs,
            },
            result: result ?? summary,
            resultFileExtension: resultExtension);
    }

    private string? GetScriptsDirectory()
    {
        if (string.IsNullOrWhiteSpace(CurrentFolder))
        {
            return null;
        }

        var scripts = Path.Combine(CurrentFolder, "scripts", "apex");
        return Directory.Exists(scripts) ? scripts : CurrentFolder;
    }

    private static string Summarize(string text)
    {
        var single = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= 80 ? single : single[..80] + "…";
    }
}
