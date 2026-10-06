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
    private readonly SObjectDescribeService _describes;
    private readonly AppSettingsStore _settings;
    private readonly AppLog _log;
    private readonly UiDebouncer _liveCountDebouncer;
    private CancellationTokenSource? _liveCountCts;

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

    public SoqlViewModel(SoqlService service, HistoryStore history, FavoritesStore favorites, IFilePickerService filePicker, IClipboardService clipboard, SObjectDescribeService describes, AppSettingsStore settings, IUiDispatcher ui, AppLog log)
    {
        _service = service;
        _history = history;
        _favorites = favorites;
        _filePicker = filePicker;
        _clipboard = clipboard;
        _describes = describes;
        _settings = settings;
        _log = log;
        _aiAssistEnabled = settings.Current.SoqlAiAssist;
        _liveCountDebouncer = new UiDebouncer(1200, ui);
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

    // ---- 候補エリア（エディターからの問い合わせ） ----

    /// <summary>候補エリアへ表示する候補一覧。</summary>
    public ObservableCollection<SoqlCompletionItem> Suggestions { get; } = new();

    /// <summary>候補エリアの見出し（例: 「Contact の項目候補:」）。</summary>
    [ObservableProperty]
    private string _suggestionsHeader = string.Empty;

    /// <summary>候補エリアを表示するか。</summary>
    [ObservableProperty]
    private bool _isSuggestionsVisible;

    /// <summary>候補を取得中か（「項目取得中…」の表示）。</summary>
    [ObservableProperty]
    private bool _isFetchingSuggestions;

    private CancellationTokenSource? _suggestCts;

    /// <summary>
    /// カーソル位置の候補を候補エリアへ反映する（SoqlView のエディターから呼ばれる）。
    /// 入力停止・カーソル移動のたびに呼ばれ、古い取得はキャンセルされる。
    /// </summary>
    public async Task UpdateSuggestionsAsync(string text, int caret)
    {
        var context = SoqlCompletionParser.Parse(text, caret);

        _suggestCts?.Cancel();
        _suggestCts?.Dispose();
        _suggestCts = null;

        if (context.InsideString
            || context.Clause is SoqlClause.None or SoqlClause.Other or SoqlClause.Limit or SoqlClause.Offset
            || string.IsNullOrWhiteSpace(CurrentOrg))
        {
            HideSuggestions();
            return;
        }

        var cts = new CancellationTokenSource();
        _suggestCts = cts;
        IsSuggestionsVisible = true;
        IsFetchingSuggestions = true;

        try
        {
            IReadOnlyList<SoqlCompletionItem> items;
            string header;

            if (context.Clause == SoqlClause.From)
            {
                header = UiText.T("Soql_SuggestObjects");
                var objects = await _describes.ListObjectsAsync(CurrentOrg!, cancellationToken: cts.Token).ConfigureAwait(true);
                items = SoqlCompletionEngine.ObjectItems(objects, context.Prefix);
            }
            else
            {
                var target = await ResolveCompletionTargetAsync(context, cts.Token).ConfigureAwait(true);
                if (target is null)
                {
                    if (!cts.IsCancellationRequested)
                    {
                        HideSuggestions();
                    }

                    return;
                }

                header = UiText.T("Soql_SuggestFieldsFmt", target);
                var describe = await _describes.DescribeAsync(CurrentOrg!, target, cancellationToken: cts.Token).ConfigureAwait(true);
                var includeFunctions = context.IncludeFunctions && context.Path.Count == 0;
                items = SoqlCompletionEngine.FieldItems(describe, context.Prefix, includeFunctions);
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            SuggestionsHeader = header;
            Suggestions.Clear();
            foreach (var item in items)
            {
                Suggestions.Add(item);
            }

            IsFetchingSuggestions = false;
            IsSuggestionsVisible = items.Count > 0;
        }
        catch (OperationCanceledException)
        {
            // 次の入力・カーソル移動で破棄された
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                HideSuggestions();
                _log.Warn($"SOQL 候補の取得に失敗: {ex.Message}");
            }
        }
    }

    private void HideSuggestions()
    {
        IsFetchingSuggestions = false;
        IsSuggestionsVisible = false;
        Suggestions.Clear();
    }

    /// <summary>項目候補の対象オブジェクトを解決する（エイリアス / 別オブジェクト / 参照関係の連鎖）。</summary>
    private async Task<string?> ResolveCompletionTargetAsync(SoqlCompletionContext context, CancellationToken cancellationToken)
    {
        var org = CurrentOrg!;
        string? target = context.FromObjects.FirstOrDefault();

        for (var i = 0; i < context.Path.Count; i++)
        {
            var segment = context.Path[i];

            if (i == 0)
            {
                if (context.Aliases.TryGetValue(segment, out var aliased))
                {
                    target = aliased;
                    continue;
                }

                var fromMatch = context.FromObjects.FirstOrDefault(o => string.Equals(o, segment, StringComparison.OrdinalIgnoreCase));
                if (fromMatch is not null)
                {
                    target = fromMatch;
                    continue;
                }
            }

            if (target is null)
            {
                return null;
            }

            var describe = await _describes.DescribeAsync(org, target, cancellationToken: cancellationToken).ConfigureAwait(true);
            var relationship = SoqlCompletionEngine.ResolveRelationship(describe, segment);
            if (relationship is null)
            {
                return null;
            }

            target = relationship;
        }

        return target;
    }

    // ---- 件数のライブ表示（入力停止後に自動更新） ----

    /// <summary>件数の表示テキスト。</summary>
    [ObservableProperty]
    private string _liveCountText = string.Empty;

    partial void OnSoqlTextChanged(string value) => _liveCountDebouncer.Debounce(() => _ = RunLiveCountAsync());

    private async Task RunLiveCountAsync()
    {
        _liveCountCts?.Cancel();
        _liveCountCts?.Dispose();
        _liveCountCts = null;

        var countQuery = SoqlCountQueryBuilder.Build(SoqlText);
        if (countQuery is null || string.IsNullOrWhiteSpace(CurrentOrg) || IsRunning)
        {
            LiveCountText = string.Empty;
            return;
        }

        var cts = new CancellationTokenSource();
        _liveCountCts = cts;
        LiveCountText = UiText.T("Soql_LiveCountBusy");
        try
        {
            var execution = await _service.ExecuteSoqlAsync(CurrentOrg, countQuery, useToolingApi: false, PreferRest, CurrentFolder, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                LiveCountText = UiText.T("Soql_LiveCountFmt", execution.Result.TotalSize);
            }
        }
        catch (OperationCanceledException)
        {
            // 入力の続きで破棄された
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                LiveCountText = string.Empty;
                _log.Warn($"SOQL 件数の取得に失敗: {ex.Message}");
            }
        }
    }

    // ---- 実行後の AI 支援 ----

    /// <summary>実行後に AI パネルへ自動送信するハンドラー（MainViewModel が設定する）。</summary>
    public Func<string, Task>? AiAssistHandler { get; set; }

    /// <summary>実行後に AI へ質問するか（settings.json の soqlAiAssist に保存）。</summary>
    [ObservableProperty]
    private bool _aiAssistEnabled;

    partial void OnAiAssistEnabledChanged(bool value)
    {
        if (_settings.Current.SoqlAiAssist != value)
        {
            _settings.Current.SoqlAiAssist = value;
            _settings.Save();
        }
    }

    private void NotifyAiAfterRun(bool success, string soql, string? error, int? totalSize)
    {
        if (!AiAssistEnabled || AiAssistHandler is not { } handler)
        {
            return;
        }

        var prompt = success
            ? UiText.T("Soql_AiPromptSuccessFmt", soql, totalSize ?? 0)
            : UiText.T("Soql_AiPromptErrorFmt", soql, error ?? string.Empty);
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
            NotifyAiAfterRun(success: true, soql, error: null, totalSize: execution.Result.TotalSize);
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
            NotifyAiAfterRun(success: false, soql, error: ex.Message, totalSize: null);
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
