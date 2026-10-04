using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>sf 自由コマンドタブの ViewModel。</summary>
public partial class CommandViewModel : ObservableObject
{
    private readonly SfCliRunner _runner;
    private readonly HistoryStore _history;
    private readonly FavoritesStore _favorites;
    private readonly AppSettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _commandText = "org list";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = UiText.T("Common_Ready");

    [ObservableProperty]
    private string? _stdOut;

    [ObservableProperty]
    private string? _stdErr;

    [ObservableProperty]
    private HistoryEntry? _selectedHistoryItem;

    /// <summary>現在の対象組織（MainViewModel から設定される）。</summary>
    public string? CurrentOrg { get; set; }

    /// <summary>現在の SF 実行フォルダ（MainViewModel から設定される）。</summary>
    public string? CurrentFolder { get; set; }

    public ObservableCollection<HistoryEntry> HistoryItems { get; } = new();

    public CommandViewModel(SfCliRunner runner, HistoryStore history, FavoritesStore favorites, AppSettingsStore settings, IDialogService dialogs, AppLog log)
    {
        _runner = runner;
        _history = history;
        _favorites = favorites;
        _settings = settings;
        _dialogs = dialogs;
        _log = log;
        RefreshHistory();
    }

    [RelayCommand]
    private void RefreshHistory()
    {
        HistoryItems.Clear();
        foreach (var entry in _history.Query(new HistoryQuery(Type: HistoryTypes.Command)).Take(30))
        {
            HistoryItems.Add(entry);
        }
    }

    partial void OnSelectedHistoryItemChanged(HistoryEntry? value)
    {
        if (value?.Params is { Length: > 0 } text)
        {
            CommandText = text;
        }
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var input = CommandText.Trim();
        if (string.IsNullOrEmpty(input))
        {
            StatusText = UiText.T("Command_Enter");
            return;
        }

        var arguments = CommandLineParser.SplitSfArguments(input);
        if (arguments.Count == 0)
        {
            StatusText = UiText.T("Command_EnterArgs");
            return;
        }

        if (SfCommandSafety.IsDangerous(arguments, out var token)
            && ConfirmPolicies.ShouldConfirm(_settings.Current.ConfirmPolicy, isDangerous: true))
        {
            var answer = _dialogs.Confirm(
                UiText.T("Command_DangerConfirmFmt", token, Environment.NewLine, string.Join(' ', arguments)),
                "SfUi");
            if (!answer)
            {
                StatusText = UiText.T("Common_Canceled");
                return;
            }
        }

        IsRunning = true;
        StatusText = UiText.T("Common_Running");
        StdOut = null;
        StdErr = null;
        _cts = new CancellationTokenSource();
        try
        {
            var raw = await _runner.RunAsync(arguments, CurrentFolder, cancellationToken: _cts.Token);
            StdOut = raw.StdOut;
            StdErr = raw.StdErr;
            StatusText = UiText.T("Common_ExitCodeFmt", raw.ExitCode, raw.Duration.TotalMilliseconds) + (raw.TimedOut ? UiText.T("Common_TimeoutSuffix") : string.Empty);
            AppendHistory(input, raw.Success ? "success" : "error", (int)raw.Duration.TotalMilliseconds, BuildResultText(raw));
            _log.Info($"コマンド実行: sf {string.Join(' ', arguments)} → 終了コード {raw.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            StatusText = UiText.T("Common_Canceled");
            AppendHistory(input, "canceled", 0, null);
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Common_FailedFmt", ex.Message);
            AppendHistory(input, "error", 0, ex.Message);
            _log.Error("コマンド実行に失敗", ex);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            RefreshHistory();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void AddFavorite()
    {
        var input = CommandText.Trim();
        if (string.IsNullOrEmpty(input))
        {
            StatusText = UiText.T("Command_NoCommandFavorite");
            return;
        }

        _favorites.Add(HistoryTypes.Command, Summarize(input), input);
        StatusText = UiText.T("Common_FavoriteAddedFmt", Summarize(input));
    }

    /// <summary>履歴から読み込む（autoRun=true ならそのまま実行）。</summary>
    public void LoadFromHistory(HistoryEntry entry, bool autoRun)
    {
        if (entry.Params is { Length: > 0 } text)
        {
            CommandText = text;
        }

        if (autoRun)
        {
            _ = ExecuteAsync();
        }
    }

    /// <summary>外部（AI チャット等）からコマンドを読み込む。</summary>
    public void LoadText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            CommandText = text;
        }
    }

    /// <summary>お気に入りから読み込む（autoRun=true ならそのまま実行）。</summary>
    public void LoadFavorite(FavoriteItem item, bool autoRun)
    {
        if (item.Payload is { Length: > 0 } text)
        {
            CommandText = text;
        }

        if (autoRun)
        {
            _ = ExecuteAsync();
        }
    }

    private void AppendHistory(string input, string status, int durationMs, string? result)
    {
        _history.Append(
            new HistoryEntry
            {
                Type = HistoryTypes.Command,
                Org = CurrentOrg,
                Folder = CurrentFolder,
                Params = input,
                Summary = Summarize(input),
                Status = status,
                DurationMs = durationMs,
            },
            result: result);
    }

    private static string BuildResultText(SfCliResult raw)
    {
        if (string.IsNullOrEmpty(raw.StdErr))
        {
            return raw.StdOut;
        }

        return raw.StdOut + Environment.NewLine + "--- stderr ---" + Environment.NewLine + raw.StdErr;
    }

    private static string Summarize(string text)
    {
        var single = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= 80 ? single : single[..80] + "…";
    }
}
