using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>匿名Apex タブの ViewModel。</summary>
public partial class ApexViewModel : ObservableObject
{
    private readonly ApexService _service;
    private readonly HistoryStore _history;
    private readonly FavoritesStore _favorites;
    private readonly IFilePickerService _filePicker;
    private readonly AppSettingsStore _settings;
    private readonly AppLog _log;

    [ObservableProperty]
    private string _apexCode = "System.debug('Hello SfUi');";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = UiText.T("Common_Ready");

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

    public ApexViewModel(ApexService service, HistoryStore history, FavoritesStore favorites, IFilePickerService filePicker, AppSettingsStore settings, AppLog log)
    {
        _service = service;
        _history = history;
        _favorites = favorites;
        _filePicker = filePicker;
        _settings = settings;
        _log = log;
        _aiAssistEnabled = settings.Current.ApexAiAssist;
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
            StatusText = UiText.T("Msg_SelectOrg");
            return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            StatusText = UiText.T("Apex_EnterCode");
            return;
        }

        IsRunning = true;
        StatusText = UiText.T("Common_Running");
        ResultSummary = null;
        ResultLogs = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _service.ExecuteAnonymousAsync(CurrentOrg, code, CurrentFolder);

            if (result.CompileProblem is { Length: > 0 } compileProblem)
            {
                StatusText = UiText.T("Apex_CompileError");
                ResultSummary = UiText.T("Apex_CompileErrorFmt", compileProblem);
                AppendHistory(code, "error", (int)result.Duration.TotalMilliseconds, ResultSummary);
                NotifyAiAfterRun(success: false, code, ResultSummary, logs: null);
            }
            else if (result.ExceptionMessage is { Length: > 0 } exceptionMessage)
            {
                StatusText = UiText.T("Apex_RuntimeException");
                ResultSummary = UiText.T("Apex_ExceptionFmt", result.Line, result.Column, exceptionMessage, Environment.NewLine + result.ExceptionStackTrace);
                ResultLogs = result.Logs;
                AppendHistory(code, "error", (int)result.Duration.TotalMilliseconds, ResultSummary, "log", result.Logs);
                NotifyAiAfterRun(success: false, code, ResultSummary, result.Logs);
            }
            else if (result.Success && result.Compiled)
            {
                StatusText = UiText.T("Apex_SuccessFmt", result.Duration.TotalMilliseconds);
                ResultSummary = result.Logs is { Length: > 0 } ? UiText.T("Apex_SuccessWithLogs") : UiText.T("Apex_Success");
                ResultLogs = result.Logs;
                AppendHistory(code, "success", (int)result.Duration.TotalMilliseconds, ResultSummary, "log", result.Logs);
                NotifyAiAfterRun(success: true, code, error: null, logs: result.Logs);
            }
            else
            {
                var message = result.ErrorMessage ?? UiText.T("Apex_ErrorUnknown");
                StatusText = UiText.T("Common_FailedFmt", message);
                ResultSummary = message;
                AppendHistory(code, "error", (int)result.Duration.TotalMilliseconds, ResultSummary);
                NotifyAiAfterRun(success: false, code, ResultSummary, result.Logs);
            }

            _log.Info($"匿名Apex 実行: {StatusText}");
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Common_FailedFmt", ex.Message);
            ResultSummary = ex.Message;
            AppendHistory(code, "error", (int)stopwatch.Elapsed.TotalMilliseconds, ResultSummary);
            NotifyAiAfterRun(success: false, code, ResultSummary, logs: null);
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
            StatusText = UiText.T("Msg_SelectOrg");
            return;
        }

        try
        {
            var logText = await _service.GetLogAsync(CurrentOrg, latestCount: 1);
            if (string.IsNullOrEmpty(logText))
            {
                StatusText = UiText.T("Apex_NoOrgLogs");
                return;
            }

            ResultLogs = logText;
            StatusText = UiText.T("Apex_LatestLogFmt", logText.Length);
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Apex_LatestLogFailedFmt", ex.Message);
            _log.Error("最新ログの取得に失敗", ex);
        }
    }

    [RelayCommand]
    private async Task LoadFileAsync()
    {
        var scriptsDirectory = GetScriptsDirectory();
        var fileName = await _filePicker.OpenFileAsync(
            UiText.T("Apex_OpenTitle"), UiText.T("Apex_OpenFilter"), scriptsDirectory);
        if (fileName is null)
        {
            return;
        }

        try
        {
            ApexCode = File.ReadAllText(fileName);
            StatusText = UiText.T("Apex_LoadedFmt", fileName);
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Apex_LoadFailedFmt", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveFileAsync()
    {
        if (string.IsNullOrWhiteSpace(ApexCode))
        {
            StatusText = UiText.T("Apex_NoCodeToSave");
            return;
        }

        var scriptsDirectory = GetScriptsDirectory();
        var fileName = await _filePicker.SaveFileAsync(
            UiText.T("Apex_SaveTitle"), $"anonymous-{DateTime.Now:yyyyMMdd-HHmmss}.apex", UiText.T("Apex_SaveFilter"), scriptsDirectory);
        if (fileName is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(fileName, ApexCode, new UTF8Encoding(false));
            StatusText = UiText.T("Common_SavedFmt", fileName);
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Common_SaveFailedFmt", ex.Message);
        }
    }

    [RelayCommand]
    private void AddFavorite()
    {
        if (string.IsNullOrWhiteSpace(ApexCode))
        {
            StatusText = UiText.T("Apex_NoCodeFavorite");
            return;
        }

        var label = Summarize(ApexCode);
        _favorites.Add(HistoryTypes.Apex, label, ApexCode);
        StatusText = UiText.T("Common_FavoriteAddedFmt", label);
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

    /// <summary>外部（AI チャット等）からコードを読み込む。</summary>
    public void LoadText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            ApexCode = text;
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

    // ---- 実行後の AI 支援 ----

    /// <summary>実行後に AI パネルへ自動送信するハンドラー（MainViewModel が設定する）。</summary>
    public Func<string, Task>? AiAssistHandler { get; set; }

    /// <summary>実行後に AI へ質問するか（settings.json の apexAiAssist に保存）。</summary>
    [ObservableProperty]
    private bool _aiAssistEnabled;

    partial void OnAiAssistEnabledChanged(bool value)
    {
        if (_settings.Current.ApexAiAssist != value)
        {
            _settings.Current.ApexAiAssist = value;
            _settings.Save();
        }
    }

    private const int MaxPromptCodeChars = 4000;
    private const int MaxPromptLogChars = 4000;
    private const int MaxPromptErrorChars = 2000;

    private void NotifyAiAfterRun(bool success, string code, string? error, string? logs)
    {
        if (!AiAssistEnabled || AiAssistHandler is not { } handler)
        {
            return;
        }

        var prompt = success
            ? UiText.T("Apex_AiPromptSuccessFmt", Truncate(code, MaxPromptCodeChars), TruncateTail(logs, MaxPromptLogChars) ?? string.Empty)
            : UiText.T("Apex_AiPromptErrorFmt", Truncate(code, MaxPromptCodeChars), Truncate(error ?? string.Empty, MaxPromptErrorChars), TruncateTail(logs, MaxPromptLogChars) ?? string.Empty);
        _ = DispatchAiAssistAsync(handler, prompt);
    }

    private async Task DispatchAiAssistAsync(Func<string, Task> handler, string prompt)
    {
        try
        {
            await handler(prompt);
        }
        catch (Exception ex)
        {
            _log.Warn($"AI への自動送信に失敗: {ex.Message}");
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "\n…(truncated)";

    private static string? TruncateTail(string? text, int max) =>
        text is null || text.Length <= max ? text : "…(truncated)\n" + text[^max..];

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
