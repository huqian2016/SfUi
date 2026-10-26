using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly IReportExportDialog _reportDialog;
    private readonly SoqlResultCacheStore? _cache;
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

    /// <summary>いま表示している結果がキャッシュ（前回の実行結果）か。</summary>
    [ObservableProperty]
    private bool _isCachedResult;

    /// <summary>キャッシュ バッジの表示文字列（例: キャッシュ · 10:24）。</summary>
    [ObservableProperty]
    private string _cachedResultText = string.Empty;

    /// <summary>キャッシュ バッジのツールチップ（取得日時と行数）。</summary>
    [ObservableProperty]
    private string _cachedResultTooltip = string.Empty;

    [ObservableProperty]
    private string _statusText = UiText.T("Common_Ready");

    /// <summary>結果グリッドの列（実行のたびに作り直す・Id は先頭固定）。</summary>
    public ObservableCollection<SoqlGridColumn> Columns { get; } = new();

    /// <summary>結果グリッドの行（実行のたびに作り直す）。</summary>
    public ObservableCollection<SoqlGridRow> Rows { get; } = new();

    /// <summary>列構成が作り直されたときに通知する（ビューの列再構築用）。</summary>
    public event Action? ResultColumnsChanged;

    /// <summary>複数 SOQL（<c>;</c> 区切り）を実行したときの結果枠（最大 20）。</summary>
    public ObservableCollection<SoqlResultPaneViewModel> Results { get; } = new();

    /// <summary>複数文のスクリプトを実行した結果を表示中か（単一文のグリッドを隠す）。</summary>
    [ObservableProperty]
    private bool _isMultiStatement;

    /// <summary>単一文の結果グリッドを表示するか（複数文のときは false）。</summary>
    public bool IsSingleStatement => !IsMultiStatement;

    partial void OnIsMultiStatementChanged(bool value) => OnPropertyChanged(nameof(IsSingleStatement));

    /// <summary>組織へ未反映の編集セル数。</summary>
    [ObservableProperty]
    private int _pendingEditCount;

    /// <summary>「組織へ反映」ボタンの表示文字列（未反映の件数付き）。</summary>
    [ObservableProperty]
    private string _deployButtonText = UiText.T("Soql_DeployFmt", 0);

    /// <summary>最後に実行したクエリの対象オブジェクト（レコードページ URL・反映に使う）。</summary>
    private string? _resultObjectName;

    [ObservableProperty]
    private HistoryEntry? _selectedHistoryItem;

    /// <summary>サブタブのタイトル（MainViewModel のタブ ホストが設定する）。</summary>
    [ObservableProperty]
    private string _tabTitle = string.Empty;

    /// <summary>現在の対象組織（MainViewModel から設定される）。</summary>
    public string? CurrentOrg { get; set; }

    /// <summary>現在の SF 実行フォルダ（MainViewModel から設定される）。</summary>
    public string? CurrentFolder { get; set; }

    public ObservableCollection<HistoryEntry> HistoryItems { get; } = new();

    public SoqlViewModel(SoqlService service, HistoryStore history, FavoritesStore favorites, IFilePickerService filePicker, IClipboardService clipboard, SObjectDescribeService describes, AppSettingsStore settings, IUiDispatcher ui, AppLog log, IReportExportDialog reportDialog, SoqlResultCacheStore? cache = null)
    {
        _service = service;
        _history = history;
        _favorites = favorites;
        _filePicker = filePicker;
        _clipboard = clipboard;
        _describes = describes;
        _settings = settings;
        _log = log;
        _reportDialog = reportDialog;
        _cache = cache;
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
        var stopwatch = Stopwatch.StartNew();
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
                var target = await SoqlCompletionEngine.ResolveTargetAsync(_describes, CurrentOrg!, context, cts.Token).ConfigureAwait(true);
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
            _log.Info($"SOQL 候補を表示: {items.Count} 件 ({stopwatch.ElapsedMilliseconds} ms)");
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

        // "--" コメントを除き ";" で複数の SOQL に分割する（1 文なら従来どおり単一の結果グリッド）
        var statements = SoqlScript.Split(soql);
        if (statements.Count == 0)
        {
            StatusText = UiText.T("Soql_EnterQuery");
            return;
        }

        if (statements.Count == 1)
        {
            await ExecuteSingleAsync(statements[0].Text, soql);
            return;
        }

        await ExecuteScriptAsync(statements, soql);
    }

    private async Task ExecuteSingleAsync(string statement, string originalSoql)
    {
        IsMultiStatement = false;
        Results.Clear();
        var soql = originalSoql;
        IsRunning = true;
        var stopwatch = Stopwatch.StartNew();

        // 結果の先頭に Id 列を固定で用意する（集計など括弧を含む選択リストでは補わない）
        var (effectiveSoql, idInjected) = SoqlQueryEditor.EnsureIdSelected(statement);

        // まず前回の結果（キャッシュ）があれば即表示し、裏で最新を取得する
        var showingCache = TryShowCachedResult(effectiveSoql, statement);
        StatusText = showingCache
            ? UiText.T("Soql_CacheRefreshingFmt", _cachedAtText)
            : UiText.T("Common_Running");

        try
        {
            var execution = await _service.ExecuteSoqlAsync(CurrentOrg, effectiveSoql, UseToolingApi, PreferRest, CurrentFolder);
            _resultObjectName = SoqlQueryEditor.TryGetObjectName(statement);
            ClearCachedResult();
            BuildResultModel(execution.Result.Table);
            StatusText = UiText.T("Soql_ResultFmt", execution.Engine, execution.Result.TotalSize, execution.Duration.TotalMilliseconds);
            if (idInjected)
            {
                _log.Info("SOQL: 選択リストに Id を補って実行しました。");
            }

            SaveCachedResult(effectiveSoql, soql, execution);

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
            // キャッシュを表示している場合はそのまま残し、更新に失敗したことを伝える
            if (IsCachedResult)
            {
                StatusText = UiText.T("Soql_CacheRefreshFailedFmt", ex.Message, _cachedAtText);
                _log.Warn($"SOQL: 最新の取得に失敗したためキャッシュを表示したままにします: {ex.Message}");
            }
            else
            {
                _resultObjectName = null;
                ClearResultModel();
                StatusText = UiText.T("Common_FailedFmt", ex.Message);
            }

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

    /// <summary>
    /// 複数の SOQL（<c>;</c> 区切り）を上から順に実行し、文ごとの結果枠を埋める。
    /// 失敗した文はその枠にエラーを表示し、残りの文は続けて実行する。
    /// </summary>
    private async Task ExecuteScriptAsync(IReadOnlyList<SoqlStatement> statements, string originalSoql)
    {
        if (CurrentOrg is not { Length: > 0 } org)
        {
            StatusText = UiText.T("Msg_SelectOrg");
            return;
        }

        IsMultiStatement = true;
        ClearResultModel();
        ClearCachedResult();
        Results.Clear();

        var panes = new List<SoqlResultPaneViewModel>(statements.Count);
        foreach (var statement in statements)
        {
            var pane = new SoqlResultPaneViewModel(statement.Index, statement.Text, OpenRecordFromPaneAsync);
            Results.Add(pane);
            panes.Add(pane);

            // キャッシュがあれば先に表示しておく（裏で最新に置き換わる）
            if (_cache is null)
            {
                continue;
            }

            try
            {
                var (effectiveSoql, _) = SoqlQueryEditor.EnsureIdSelected(statement.Text);
                var entry = _cache.Find(org, effectiveSoql, UseToolingApi);
                var cached = entry is null ? null : SoqlResultCacheStore.ToResult(entry);
                if (entry is not null && cached is not null)
                {
                    pane.SetCached(cached.Table, entry.CachedAt);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"SOQL: キャッシュの読み込みに失敗: {ex.Message}");
            }
        }

        IsRunning = true;
        var stopwatch = Stopwatch.StartNew();
        var succeeded = 0;
        var failed = 0;
        string? firstError = null;
        try
        {
            for (var i = 0; i < statements.Count; i++)
            {
                var statement = statements[i];
                var pane = panes[i];
                StatusText = UiText.T("Soql_ScriptRunningFmt", i + 1, statements.Count);
                if (!pane.IsCachedResult)
                {
                    pane.SetRunning();
                }

                try
                {
                    var (effectiveSoql, idInjected) = SoqlQueryEditor.EnsureIdSelected(statement.Text);
                    var execution = await _service.ExecuteSoqlAsync(org, effectiveSoql, UseToolingApi, PreferRest, CurrentFolder);
                    pane.SetResult(execution.Result.Table, UiText.T("Soql_ResultFmt", execution.Engine, execution.Result.TotalSize, execution.Duration.TotalMilliseconds));
                    if (idInjected)
                    {
                        _log.Info($"SOQL: 文 {i + 1} の選択リストに Id を補って実行しました。");
                    }

                    SaveCachedResult(effectiveSoql, statement.Text, execution);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    pane.SetError(ex.Message);
                    failed++;
                    firstError ??= ex.Message;
                    _log.Error($"SOQL: 文 {i + 1} の実行に失敗", ex);
                }
            }

            stopwatch.Stop();
            StatusText = failed == 0
                ? UiText.T("Soql_ScriptDoneFmt", succeeded)
                : UiText.T("Soql_ScriptPartialFmt", succeeded, failed, firstError ?? string.Empty);
            _log.Info($"SOQL スクリプト実行: {StatusText}");

            _history.Append(
                new HistoryEntry
                {
                    Type = HistoryTypes.Soql,
                    Org = CurrentOrg,
                    Folder = CurrentFolder,
                    Params = originalSoql,
                    Summary = UiText.T("Soql_ScriptSummaryFmt", statements.Count, Summarize(statements[0].Text)),
                    Status = failed == 0 ? "success" : "error",
                    DurationMs = (int)stopwatch.Elapsed.TotalMilliseconds,
                },
                result: string.Join("\n--\n", panes.Select(pane => pane.HasError ? $"ERROR: {pane.ErrorText}" : $"[{pane.Rows.Count} rows]")));
        }
        finally
        {
            IsRunning = false;
            RefreshHistory();
        }
    }

    /// <summary>結果枠の Id リンクからレコードページを開く。</summary>
    private Task OpenRecordFromPaneAsync(SoqlGridRow row) => OpenRecordAsync(row);

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var table = GetCurrentTable();
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
    private async Task ExportReportAsync()
    {
        var table = GetCurrentTable();
        if (table is null || table.Rows.Count == 0)
        {
            StatusText = UiText.T("Report_NoData");
            return;
        }

        var report = ReportDocumentFactory.FromTable(
            UiText.T("Report_SoqlTitle"),
            CurrentOrg,
            SoqlText,
            ReportDocumentFactory.TableFromDataTable("SOQL", table),
            new ReportSection { Heading = UiText.T("Report_QueryHeading"), Text = SoqlText });
        var initialDirectory = !string.IsNullOrWhiteSpace(CurrentFolder) && Directory.Exists(CurrentFolder) ? CurrentFolder : null;
        var outcome = await ReportExportRunner.RunAsync(
            _reportDialog,
            report,
            $"soql-{DateTime.Now:yyyyMMdd-HHmmss}",
            initialDirectory);
        if (outcome.Canceled)
        {
            return;
        }

        StatusText = outcome.Error is null
            ? ReportExportRunner.DescribeSuccess(outcome, initialDirectory ?? string.Empty)
            : UiText.T("Report_FailedFmt", outcome.Error);
    }

    [RelayCommand]
    private void CopyResults()
    {
        var table = GetCurrentTable();
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

    // ---- 結果グリッド（Id 列の固定・セル編集・組織へ反映） ----

    /// <summary>キャッシュ表示中の取得時刻（バッジ・ステータス用）。</summary>
    private string _cachedAtText = string.Empty;

    /// <summary>
    /// 前回の実行結果（キャッシュ）があれば即座に表示する。
    /// 表示できたときは true を返し、裏で最新を取得したら <see cref="ClearCachedResult"/> で消す。
    /// </summary>
    private bool TryShowCachedResult(string effectiveSoql, string originalSoql)
    {
        ClearCachedResult();
        if (_cache is null || string.IsNullOrWhiteSpace(CurrentOrg))
        {
            return false;
        }

        SoqlCacheEntry? entry;
        SoqlResult? result;
        try
        {
            entry = _cache.Find(CurrentOrg, effectiveSoql, UseToolingApi);
            result = entry is null ? null : SoqlResultCacheStore.ToResult(entry);
        }
        catch (Exception ex)
        {
            _log.Warn($"SOQL: キャッシュの読み込みに失敗: {ex.Message}");
            return false;
        }

        if (entry is null || result is null)
        {
            return false;
        }

        _resultObjectName = SoqlQueryEditor.TryGetObjectName(originalSoql);
        BuildResultModel(result.Table);

        _cachedAtText = entry.CachedAt.LocalDateTime.ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture);
        IsCachedResult = true;
        CachedResultText = UiText.T("Soql_CacheBadgeFmt", _cachedAtText);
        CachedResultTooltip = UiText.T(
            "Soql_CacheTooltipFmt",
            entry.CachedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture),
            entry.RowCount);
        _log.Info($"SOQL: キャッシュした結果を表示しました（{entry.RowCount} 行 / {_cachedAtText} 取得）。最新を取得します。");
        return true;
    }

    /// <summary>キャッシュ表示のバッジを消す（最新の結果に置き換わったとき）。</summary>
    private void ClearCachedResult()
    {
        _cachedAtText = string.Empty;
        IsCachedResult = false;
        CachedResultText = string.Empty;
        CachedResultTooltip = string.Empty;
    }

    /// <summary>実行結果を次回表示用にキャッシュへ保存する。</summary>
    private void SaveCachedResult(string effectiveSoql, string originalSoql, SoqlExecution execution)
    {
        if (_cache is null || string.IsNullOrWhiteSpace(CurrentOrg))
        {
            return;
        }

        try
        {
            // 大きすぎる結果はキャッシュしない（古いキャッシュも消す）
            if (execution.Result.RowCount > SoqlResultCacheStore.MaxRows ||
                execution.Result.RawJson.Length > SoqlResultCacheStore.MaxRawBytes)
            {
                _cache.Remove(CurrentOrg, effectiveSoql, UseToolingApi);
                return;
            }

            _cache.Save(CurrentOrg, new SoqlCacheEntry
            {
                Query = SoqlResultCacheStore.NormalizeQuery(effectiveSoql),
                DisplayQuery = originalSoql,
                UseToolingApi = UseToolingApi,
                Engine = execution.Engine,
                TotalSize = execution.Result.TotalSize,
                Done = execution.Result.Done,
                RowCount = execution.Result.RowCount,
                CachedAt = DateTimeOffset.Now,
                RawJson = execution.Result.RawJson,
            });
        }
        catch (Exception ex)
        {
            _log.Warn($"SOQL: 結果のキャッシュ保存に失敗: {ex.Message}");
        }
    }

    /// <summary>現在の表示内容（未反映の編集を含む）を DataTable 化する（CSV/コピー用）。</summary>
    private DataTable? GetCurrentTable()
    {
        if (Columns.Count == 0)
        {
            return null;
        }

        var table = new DataTable();
        foreach (var column in Columns)
        {
            table.Columns.Add(column.Key, typeof(string));
        }

        foreach (var row in Rows)
        {
            var values = table.NewRow();
            for (var i = 0; i < Columns.Count && i < row.Cells.Count; i++)
            {
                values[i] = (object?)row.Cells[i].Value ?? DBNull.Value;
            }

            table.Rows.Add(values);
        }

        return table;
    }

    /// <summary>実行結果から結果グリッドを作り直す。</summary>
    private void BuildResultModel(DataTable table)
    {
        ClearResultModel();

        var (columns, rows) = SoqlResultModelBuilder.Build(table);
        foreach (var column in columns)
        {
            Columns.Add(column);
        }

        foreach (var row in rows)
        {
            foreach (var cell in row.Cells)
            {
                if (!cell.IsReadOnly)
                {
                    cell.PropertyChanged += OnCellPropertyChanged;
                }
            }

            Rows.Add(row);
        }

        ResultColumnsChanged?.Invoke();
    }

    /// <summary>結果グリッドを空にする（イベント解除込み）。</summary>
    private void ClearResultModel()
    {
        foreach (var row in Rows)
        {
            foreach (var cell in row.Cells)
            {
                cell.PropertyChanged -= OnCellPropertyChanged;
            }
        }

        Rows.Clear();
        Columns.Clear();
        PendingEditCount = 0;
        ResultColumnsChanged?.Invoke();
    }

    private void OnCellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SoqlCell.IsDirty) || sender is not SoqlCell cell)
        {
            return;
        }

        PendingEditCount = Math.Max(0, PendingEditCount + (cell.IsDirty ? 1 : -1));
    }

    partial void OnPendingEditCountChanged(int value)
    {
        DeployButtonText = UiText.T("Soql_DeployFmt", value);
        DeployCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value) => DeployCommand.NotifyCanExecuteChanged();

    private bool CanDeploy() => PendingEditCount > 0 && !IsRunning;

    /// <summary>未反映の編集がある行を集める。</summary>
    private List<SoqlEditRow> CollectEditedRows()
    {
        var edits = new List<SoqlEditRow>();
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            Dictionary<string, string?>? fields = null;
            for (var c = 0; c < row.Cells.Count && c < Columns.Count; c++)
            {
                var cell = row.Cells[c];
                if (cell.IsDirty)
                {
                    (fields ??= new Dictionary<string, string?>(StringComparer.Ordinal))[Columns[c].Key] = cell.Value;
                }
            }

            if (fields is not null)
            {
                edits.Add(new SoqlEditRow(i, row.RecordId, fields));
            }
        }

        return edits;
    }

    private void MarkRowFailed(int rowIndex, string error)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count)
        {
            return;
        }

        foreach (var cell in Rows[rowIndex].Cells)
        {
            if (cell.IsDirty)
            {
                cell.Error = error;
            }
        }
    }

    /// <summary>対象オブジェクトの describe（無い場合は null = 文字列のまま送信）。</summary>
    private async Task<IReadOnlyDictionary<string, DataIoField>?> FetchDescribeFieldsAsync(string objectName)
    {
        try
        {
            var describe = await _describes.DescribeAsync(CurrentOrg!, objectName);
            return describe.Fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _log.Warn($"describe を取得できないため文字列のまま送信します: {ex.Message}");
            return null;
        }
    }

    /// <summary>編集済みセルを組織へ反映する（composite/sobjects・Tooling API は 1 件ずつ）。</summary>
    [RelayCommand(CanExecute = nameof(CanDeploy))]
    private async Task DeployAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentOrg))
        {
            StatusText = UiText.T("Msg_SelectOrg");
            return;
        }

        var edits = CollectEditedRows();
        if (edits.Count == 0)
        {
            StatusText = UiText.T("Soql_DeployNoChanges");
            return;
        }

        if (string.IsNullOrEmpty(_resultObjectName))
        {
            StatusText = UiText.T("Soql_Err_ObjectUnknown");
            return;
        }

        IsRunning = true;
        StatusText = UiText.T("Soql_DeployBusy");
        try
        {
            var fields = await FetchDescribeFieldsAsync(_resultObjectName);
            var plan = SoqlEditPlanner.Plan(edits, fields);

            var planned = new List<ImportPlannedRow>();
            var failed = 0;
            string? firstError = null;
            foreach (var entry in plan)
            {
                if (entry.Error is { } error)
                {
                    failed++;
                    firstError ??= error;
                    MarkRowFailed(entry.RowIndex, error);
                    continue;
                }

                if (entry.Planned is { } row)
                {
                    planned.Add(row);
                }
            }

            var succeeded = 0;
            if (planned.Count > 0)
            {
                var results = await _service.UpdateRecordsAsync(CurrentOrg, _resultObjectName, planned, UseToolingApi);
                foreach (var result in results)
                {
                    if (result.Success)
                    {
                        succeeded++;
                        if (result.RowIndex >= 0 && result.RowIndex < Rows.Count)
                        {
                            foreach (var cell in Rows[result.RowIndex].Cells)
                            {
                                cell.Commit();
                            }
                        }
                    }
                    else
                    {
                        failed++;
                        firstError ??= result.Error;
                        MarkRowFailed(result.RowIndex, result.Error ?? string.Empty);
                    }
                }
            }

            StatusText = failed == 0
                ? UiText.T("Soql_DeployDoneFmt", succeeded)
                : succeeded > 0
                    ? UiText.T("Soql_DeployPartialFmt", succeeded, failed, firstError ?? string.Empty)
                    : UiText.T("Soql_DeployFailedFmt", firstError ?? string.Empty);
            _log.Info($"SOQL 反映: {StatusText}");

            // 反映でデータが変わったため、このクエリのキャッシュは古くなった
            if (succeeded > 0)
            {
                InvalidateCacheForCurrentQuery();
            }
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Soql_DeployFailedFmt", ex.Message);
            _log.Error("SOQL の組織反映に失敗", ex);
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>組織へ反映した結果が古くならないよう、現在のクエリのキャッシュを破棄する。</summary>
    private void InvalidateCacheForCurrentQuery()
    {
        if (_cache is null || string.IsNullOrWhiteSpace(CurrentOrg))
        {
            return;
        }

        try
        {
            var (effectiveSoql, _) = SoqlQueryEditor.EnsureIdSelected(SoqlText.Trim());
            _cache.Remove(CurrentOrg, effectiveSoql, UseToolingApi);
        }
        catch (Exception ex)
        {
            _log.Warn($"SOQL: キャッシュの破棄に失敗: {ex.Message}");
        }
    }

    /// <summary>Id セルのクリックでレコードページをブラウザーで開く。</summary>
    [RelayCommand]
    private async Task OpenRecordAsync(SoqlGridRow? row)
    {
        if (string.IsNullOrWhiteSpace(CurrentOrg) || string.IsNullOrWhiteSpace(row?.RecordId))
        {
            return;
        }

        if (string.IsNullOrEmpty(_resultObjectName))
        {
            StatusText = UiText.T("Soql_Err_ObjectUnknown");
            return;
        }

        var instanceUrl = await _service.GetInstanceUrlAsync(CurrentOrg);
        var url = BackupOrgUrls.BuildRecordUrl(instanceUrl, _resultObjectName, row.RecordId);
        if (url is null)
        {
            StatusText = UiText.T("Msg_NoInstanceUrl");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            StatusText = UiText.T("Msg_BrowserFmt", url);
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Msg_BrowserFailedFmt", ex.Message);
            _log.Error("レコードページを開けませんでした", ex);
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
