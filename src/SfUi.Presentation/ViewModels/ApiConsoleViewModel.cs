using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>REST コンソールタブの ViewModel。</summary>
public partial class ApiConsoleViewModel : ObservableObject
{
    private const string BodySeparator = "\n---body---\n";

    private readonly SalesforceRestClient _client;
    private readonly HistoryStore _history;
    private readonly FavoritesStore _favorites;
    private readonly AppSettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;

    [ObservableProperty]
    private string _method = "GET";

    [ObservableProperty]
    private string _path = "/services/data/v67.0/limits";

    [ObservableProperty]
    private string? _requestBody;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = UiText.T("Common_Ready");

    [ObservableProperty]
    private string? _responseText;

    [ObservableProperty]
    private HistoryEntry? _selectedHistoryItem;

    /// <summary>現在の対象組織（MainViewModel から設定される）。</summary>
    public string? CurrentOrg { get; set; }

    public ObservableCollection<string> Methods { get; } = new() { "GET", "POST", "PATCH", "DELETE" };

    public ObservableCollection<HistoryEntry> HistoryItems { get; } = new();

    public ApiConsoleViewModel(SalesforceRestClient client, HistoryStore history, FavoritesStore favorites, AppSettingsStore settings, IDialogService dialogs, AppLog log)
    {
        _client = client;
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
        foreach (var entry in _history.Query(new HistoryQuery(Type: HistoryTypes.Api)).Take(30))
        {
            HistoryItems.Add(entry);
        }
    }

    partial void OnSelectedHistoryItemChanged(HistoryEntry? value)
    {
        if (value is not null)
        {
            LoadFromHistory(value, autoRun: false);
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsRunning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentOrg))
        {
            StatusText = UiText.T("Msg_SelectOrg");
            return;
        }

        var path = Path.Trim();
        if (string.IsNullOrEmpty(path))
        {
            StatusText = UiText.T("Api_EnterPath");
            return;
        }

        var method = Method.Trim().ToUpperInvariant();
        var body = string.IsNullOrWhiteSpace(RequestBody) ? null : RequestBody;

        if (method != "GET" && ConfirmPolicies.ShouldConfirm(_settings.Current.ConfirmPolicy, isDangerous: true))
        {
        var answer = _dialogs.Confirm(
            UiText.T("Api_SendConfirmFmt", method, Environment.NewLine, path),
            "SfUi");
        if (!answer)
        {
            StatusText = UiText.T("Common_Canceled");
            return;
        }
        }

        IsRunning = true;
        StatusText = UiText.T("Api_Sending");
        ResponseText = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var (statusCode, responseBody) = await _client.SendConsoleAsync(CurrentOrg, new HttpMethod(method), path, body);
            var bytes = Encoding.UTF8.GetByteCount(responseBody);
            var success = (int)statusCode is >= 200 and <= 299;
            StatusText = $"HTTP {(int)statusCode} ({statusCode}) / {bytes:N0} bytes / {stopwatch.Elapsed.TotalMilliseconds:F0} ms";
            ResponseText = JsonPretty.Prettify(responseBody);
            AppendHistory(method, path, body, success ? "success" : "error", (int)stopwatch.Elapsed.TotalMilliseconds, responseBody);
            _log.Info($"REST: {method} {path} → HTTP {(int)statusCode}");
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Common_FailedFmt", ex.Message);
            ResponseText = ex.Message;
            AppendHistory(method, path, body, "error", (int)stopwatch.Elapsed.TotalMilliseconds, ex.Message);
            _log.Error("REST リクエストに失敗", ex);
        }
        finally
        {
            IsRunning = false;
            RefreshHistory();
        }
    }

    [RelayCommand]
    private void AddFavorite()
    {
        var path = Path.Trim();
        if (string.IsNullOrEmpty(path))
        {
            StatusText = UiText.T("Api_NoPathFavorite");
            return;
        }

        var label = Summarize($"{Method.Trim().ToUpperInvariant()} {path}");
        _favorites.Add(HistoryTypes.Api, label, FormatParams(Method.Trim().ToUpperInvariant(), path, string.IsNullOrWhiteSpace(RequestBody) ? null : RequestBody));
        StatusText = UiText.T("Common_FavoriteAddedFmt", label);
    }

    /// <summary>履歴から読み込む（autoRun=true ならそのまま送信）。</summary>
    public void LoadFromHistory(HistoryEntry entry, bool autoRun)
    {
        if (entry.Params is { Length: > 0 } text)
        {
            ApplyParams(text);
        }

        if (autoRun)
        {
            _ = SendAsync();
        }
    }

    /// <summary>お気に入りから読み込む（autoRun=true ならそのまま送信）。</summary>
    public void LoadFavorite(FavoriteItem item, bool autoRun)
    {
        if (item.Payload is { Length: > 0 } text)
        {
            ApplyParams(text);
        }

        if (autoRun)
        {
            _ = SendAsync();
        }
    }

    private void ApplyParams(string text)
    {
        var parts = text.Split(BodySeparator, 2, StringSplitOptions.None);
        var firstLine = parts[0].Trim();
        var spaceIndex = firstLine.IndexOf(' ');
        if (spaceIndex > 0)
        {
            Method = firstLine[..spaceIndex].ToUpperInvariant();
            Path = firstLine[(spaceIndex + 1)..].Trim();
        }

        RequestBody = parts.Length > 1 ? parts[1] : null;
    }

    private void AppendHistory(string method, string path, string? body, string status, int durationMs, string? result)
    {
        _history.Append(
            new HistoryEntry
            {
                Type = HistoryTypes.Api,
                Org = CurrentOrg,
                Params = FormatParams(method, path, body),
                Summary = Summarize($"{method} {path}"),
                Status = status,
                DurationMs = durationMs,
            },
            result: result);
    }

    private static string FormatParams(string method, string path, string? body)
    {
        var text = $"{method} {path}";
        return body is null ? text : text + BodySeparator + body;
    }

    private static string Summarize(string text) => text.Length <= 80 ? text : text[..80] + "…";
}
