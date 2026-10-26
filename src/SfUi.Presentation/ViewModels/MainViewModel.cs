using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>メインウィンドウ（上部バー・ステータスバー）の ViewModel。</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly OrgService _orgService;
    private readonly AppLog _log;
    private readonly AppSettingsStore _settings;
    private readonly RecentFoldersStore _recentFolders;
    private readonly RecentUrlsStore _recentUrls;
    private readonly ToolLauncherService _toolLauncher;
    private readonly HistoryStore _history;
    private readonly IAppWindowService _windows;
    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly EditorTabsStore _editorTabs;
    private readonly OrgCacheStore _orgCache;
    private readonly SObjectDescribeService _describes;
    private readonly OrgMetadataService _metadata;
    private readonly IUiDispatcher _ui;
    private readonly UiDebouncer _tabsSaveDebouncer;
    private CancellationTokenSource? _backgroundOrgsCts;
    private bool _tabsReady;

    /// <summary>組織一覧を取得中（取得インジケーター表示と各種操作のブロックに使う）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectOrg))]
    [NotifyPropertyChangedFor(nameof(CanUseOrgFeature))]
    [NotifyPropertyChangedFor(nameof(CanCompareOrgs))]
    [NotifyPropertyChangedFor(nameof(HasNoOrgs))]
    [NotifyPropertyChangedFor(nameof(OrgHintText))]
    [NotifyPropertyChangedFor(nameof(HasOrgHint))]
    private bool _isLoadingOrgs;

    [ObservableProperty]
    private string _statusMessage = UiText.T("Common_Ready");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDetail))]
    [NotifyPropertyChangedFor(nameof(HasSelectedOrg))]
    [NotifyPropertyChangedFor(nameof(CanUseOrgFeature))]
    [NotifyPropertyChangedFor(nameof(OrgHintText))]
    [NotifyPropertyChangedFor(nameof(HasOrgHint))]
    private OrgInfo? _selectedOrg;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDetail))]
    private string? _selectedFolder;

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>クイックパネル（左サイド）の表示状態（既定は非表示）。</summary>
    [ObservableProperty]
    private bool _isQuickPanelVisible;

    /// <summary>AI パネル（右サイド）の表示状態。</summary>
    [ObservableProperty]
    private bool _isAiPanelVisible = true;

    /// <summary>言語コンボの選択値（English / 日本語 / 简体中文 / 한국어）。</summary>
    [ObservableProperty]
    private string _languageLabel = "English";

    /// <summary>言語コンボの選択肢。</summary>
    public IReadOnlyList<string> LanguageLabels { get; } = new[] { "English", "日本語", "简体中文", "한국어" };

    /// <summary>ステータスバー表示用（組織・フォルダ）。</summary>
    public string StatusDetail => UiText.T("Main_StatusDetailFmt", SelectedOrg?.DisplayName ?? UiText.T("Main_NotSelected"), SelectedFolder ?? UiText.T("Main_DefaultFolder"));

    /// <summary>組織が選択されているか（組織情報ボタンの有効化）。</summary>
    public bool HasSelectedOrg => SelectedOrg is not null;

    /// <summary>組織コンボ・更新ボタンが使えるか（組織一覧の取得中は不可）。</summary>
    public bool CanSelectOrg => !IsLoadingOrgs;

    /// <summary>選択中組織を使う機能（組織情報・データ入出力・バックアップ）が使えるか。</summary>
    public bool CanUseOrgFeature => !IsLoadingOrgs && HasSelectedOrg;

    /// <summary>組織比較を開けるか（取得中は不可・2 組織以上が必要）。</summary>
    public bool CanCompareOrgs => !IsLoadingOrgs && Orgs.Count > 1;

    /// <summary>組織が 1 件もない（取得完了後）。</summary>
    public bool HasNoOrgs => !IsLoadingOrgs && Orgs.Count == 0;

    /// <summary>組織関連の操作ができない理由（null = 制限なし。上部バーのヒント行に表示）。</summary>
    public string? OrgHintText =>
        IsLoadingOrgs ? UiText.T("Msg_OrgsLoadingWait")
        : Orgs.Count == 0 ? UiText.T("Main_NoOrgsHint")
        : SelectedOrg is null ? UiText.T("Main_OrgRequiredHint")
        : null;

    /// <summary>理由ヒントを表示するか。</summary>
    public bool HasOrgHint => OrgHintText is not null;

    /// <summary>認証済み組織（既定組織が先頭）。</summary>
    public ObservableCollection<OrgInfo> Orgs { get; } = new();

    /// <summary>最近使用した SF 実行フォルダ（ピン留め優先）。</summary>
    public ObservableCollection<RecentFolder> RecentFolders { get; } = new();

    /// <summary>最近開いた URL（ブラウザメニュー用）。</summary>
    public ObservableCollection<RecentUrl> RecentUrls { get; } = new();

    /// <summary>履歴タブの ViewModel。</summary>
    public HistoryViewModel History { get; }

    /// <summary>SOQL サブタブのホスト（複数のクエリを並行して扱える）。</summary>
    public EditorTabHost<SoqlViewModel> SoqlTabs { get; }

    /// <summary>匿名Apex サブタブのホスト。</summary>
    public EditorTabHost<ApexViewModel> ApexTabs { get; }

    /// <summary>コマンド サブタブのホスト。</summary>
    public EditorTabHost<CommandViewModel> CommandTabs { get; }

    /// <summary>選択中の SOQL タブ（実行・履歴・お気に入り・AI 適用の対象）。</summary>
    public SoqlViewModel Soql => SoqlTabs.SelectedTab!;

    /// <summary>選択中の匿名Apex タブ。</summary>
    public ApexViewModel Apex => ApexTabs.SelectedTab!;

    /// <summary>AI チャットタブの ViewModel。</summary>
    public AiChatViewModel Ai { get; }

    /// <summary>デバッグログタブの ViewModel。</summary>
    public LogViewModel Logs { get; }

    /// <summary>選択中のコマンド タブ。</summary>
    public CommandViewModel Command => CommandTabs.SelectedTab!;

    /// <summary>REST コンソールタブの ViewModel。</summary>
    public ApiConsoleViewModel Api { get; }

    /// <summary>デプロイタブの ViewModel。</summary>
    public DeployViewModel Deploy { get; }

    /// <summary>設定タブの ViewModel。</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>クイックパネル（お気に入り）の ViewModel。</summary>
    public QuickPanelViewModel QuickPanel { get; }

    public MainViewModel(
        OrgService orgService,
        AppLog log,
        AppSettingsStore settings,
        RecentFoldersStore recentFolders,
        RecentUrlsStore recentUrls,
        ToolLauncherService toolLauncher,
        HistoryStore historyStore,
        HistoryViewModel history,
        Func<SoqlViewModel> soqlFactory,
        Func<ApexViewModel> apexFactory,
        AiChatViewModel ai,
        LogViewModel logs,
        Func<CommandViewModel> commandFactory,
        ApiConsoleViewModel api,
        DeployViewModel deploy,
        SettingsViewModel settingsViewModel,
        QuickPanelViewModel quickPanel,
        IAppWindowService windows,
        IFilePickerService filePicker,
        IDialogService dialogs,
        IClipboardService clipboard,
        EditorTabsStore editorTabsStore,
        OrgCacheStore orgCacheStore,
        SObjectDescribeService describes,
        OrgMetadataService metadata,
        IUiDispatcher ui)
    {
        _orgService = orgService;
        _log = log;
        _settings = settings;
        _recentFolders = recentFolders;
        _recentUrls = recentUrls;
        _toolLauncher = toolLauncher;
        _history = historyStore;
        _windows = windows;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _editorTabs = editorTabsStore;
        _orgCache = orgCacheStore;
        _describes = describes;
        _metadata = metadata;
        _ui = ui;
        _tabsSaveDebouncer = new UiDebouncer(1200, ui);
        Orgs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanCompareOrgs));
            OnPropertyChanged(nameof(HasNoOrgs));
            OnPropertyChanged(nameof(OrgHintText));
            OnPropertyChanged(nameof(HasOrgHint));
        };
        UiText.LanguageChanged += OnLanguageChanged;
        History = history;
        Ai = ai;
        Logs = logs;
        Api = api;
        Deploy = deploy;
        Settings = settingsViewModel;
        QuickPanel = quickPanel;
        History.ReplayRequested += OnReplayRequested;
        QuickPanel.ExecuteRequested += ExecuteFavorite;
        Ai.ApplyRequested += ApplyAiSnippet;

        // サブタブ ホスト（SOQL / 匿名Apex / コマンド）。タブごとに独立した ViewModel を生成し、
        // 組織・フォルダ・AI 支援ハンドラーを各タブへ配線する。
        SoqlTabs = new EditorTabHost<SoqlViewModel>(
            () => CreateSoqlTab(soqlFactory),
            (vm, title) => vm.TabTitle = title,
            "Tab_Soql",
            vm => vm.SoqlText,
            (vm, text) => vm.SoqlText = text,
            vm => vm.TabTitle)
        {
            TitlePrompt = tab => _dialogs.Prompt(UiText.T("Main_RenameTabTitle"), UiText.T("Main_RenameTabPrompt"), tab.TabTitle),
        };
        ApexTabs = new EditorTabHost<ApexViewModel>(
            () => CreateApexTab(apexFactory),
            (vm, title) => vm.TabTitle = title,
            "Tab_Apex",
            vm => vm.ApexCode,
            (vm, text) => vm.ApexCode = text);
        CommandTabs = new EditorTabHost<CommandViewModel>(
            () => CreateCommandTab(commandFactory),
            (vm, title) => vm.TabTitle = title,
            "Tab_Command",
            vm => vm.CommandText,
            (vm, text) => vm.CommandText = text);

        // 保存済みのサブタブ状態（本文・選択）を復元し、以降の変更を保存対象にする。
        RestoreEditorTabs();
        SoqlTabs.Changed += ScheduleTabsSave;
        ApexTabs.Changed += ScheduleTabsSave;
        CommandTabs.Changed += ScheduleTabsSave;
        _tabsReady = true;

        // 保存済みの言語をコンボへ反映（起動時の UiText 適用は App 側で実施済み）
        _languageLabel = LanguageLabelForCode(_settings.Current.Language);
        OnPropertyChanged(nameof(LanguageLabel));
        _isAiPanelVisible = _settings.Current.AiPanelVisible;
        OnPropertyChanged(nameof(IsAiPanelVisible));
    }

    /// <summary>新しい SOQL タブを作成し、現在の組織・フォルダと AI 支援を配線する。</summary>
    private SoqlViewModel CreateSoqlTab(Func<SoqlViewModel> factory)
    {
        var vm = factory();
        vm.CurrentOrg = SelectedOrg?.Alias ?? SelectedOrg?.Username;
        vm.CurrentFolder = SelectedFolder;
        vm.AiAssistHandler = async prompt =>
        {
            IsAiPanelVisible = true;
            await Ai.AskAsync(prompt);
        };
        vm.PropertyChanged += OnSoqlTabPropertyChanged;
        return vm;
    }

    /// <summary>新しい匿名Apex タブを作成し、現在の組織・フォルダと AI 支援を配線する。</summary>
    private ApexViewModel CreateApexTab(Func<ApexViewModel> factory)
    {
        var vm = factory();
        vm.CurrentOrg = SelectedOrg?.Alias ?? SelectedOrg?.Username;
        vm.CurrentFolder = SelectedFolder;
        vm.AiAssistHandler = async prompt =>
        {
            IsAiPanelVisible = true;
            await Ai.AskAsync(prompt);
        };
        vm.PropertyChanged += OnApexTabPropertyChanged;
        return vm;
    }

    /// <summary>新しいコマンド タブを作成し、現在の組織・フォルダを配線する。</summary>
    private CommandViewModel CreateCommandTab(Func<CommandViewModel> factory)
    {
        var vm = factory();
        vm.CurrentOrg = SelectedOrg?.Alias ?? SelectedOrg?.Username;
        vm.CurrentFolder = SelectedFolder;
        vm.PropertyChanged += OnCommandTabPropertyChanged;
        return vm;
    }

    /// <summary>保存済みのサブタブ状態（本文・選択）を復元する。無い場合は既定の 1 タブ。</summary>
    private void RestoreEditorTabs()
    {
        var state = _editorTabs.Load();
        RestoreHost(SoqlTabs, state.Soql);
        RestoreHost(ApexTabs, state.Apex);
        RestoreHost(CommandTabs, state.Command);
    }

    private static void RestoreHost<T>(EditorTabHost<T> host, EditorTabHostState? state) where T : class
    {
        if (state?.Tabs is { Count: > 0 })
        {
            host.RestoreFromTexts(state.Tabs, state.Selected);
            host.RestoreTitles(state.Titles);
        }
        else
        {
            host.AddTab();
        }
    }

    private void OnSoqlTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SoqlViewModel.SoqlText))
        {
            ScheduleTabsSave();
        }
    }

    private void OnApexTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApexViewModel.ApexCode))
        {
            ScheduleTabsSave();
        }
    }

    private void OnCommandTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandViewModel.CommandText))
        {
            ScheduleTabsSave();
        }
    }

    /// <summary>変更をまとめて保存する（連続入力はデバウンス）。</summary>
    private void ScheduleTabsSave()
    {
        if (!_tabsReady)
        {
            return;
        }

        _tabsSaveDebouncer.Debounce(SaveEditorTabs);
    }

    /// <summary>サブタブの状態（本文 + 選択位置）を保存する（ウィンドウ終了時にも呼ばれる）。</summary>
    public void SaveEditorTabs()
    {
        try
        {
            _editorTabs.Save(new EditorTabsFile
            {
                Soql = ToEditorTabsState(SoqlTabs),
                Apex = ToEditorTabsState(ApexTabs),
                Command = ToEditorTabsState(CommandTabs),
            });
        }
        catch (Exception ex)
        {
            _log.Warn($"エディタ タブの保存に失敗: {ex.Message}");
        }
    }

    private static EditorTabHostState ToEditorTabsState<T>(EditorTabHost<T> host) where T : class => new()
    {
        Tabs = host.ExportTexts(),
        Titles = host.ExportTitles(),
        Selected = host.SelectedIndex,
    };

    partial void OnIsAiPanelVisibleChanged(bool value)
    {
        if (_settings.Current.AiPanelVisible != value)
        {
            _settings.Current.AiPanelVisible = value;
            _settings.Save();
        }
    }

    partial void OnLanguageLabelChanged(string value)
    {
        var code = LanguageCodeForLabel(value);
        if (code == UiText.Language)
        {
            return;
        }

        UiText.SetLanguage(code);
        _settings.Current.Language = code;
        _settings.Save();
        StatusMessage = UiText.T("Msg_LanguageFmt", value);
    }

    /// <summary>言語コンボの表示名 → 言語コード。</summary>
    private static string LanguageCodeForLabel(string label) => label switch
    {
        "日本語" => UiText.Japanese,
        "简体中文" => UiText.Chinese,
        "한국어" => UiText.Korean,
        _ => UiText.English,
    };

    /// <summary>言語コード → 言語コンボの表示名。</summary>
    private static string LanguageLabelForCode(string? code) => (code ?? UiText.English).Trim().ToLowerInvariant() switch
    {
        UiText.Japanese => "日本語",
        UiText.Chinese => "简体中文",
        UiText.Korean => "한국어",
        _ => "English",
    };

    /// <summary>
    /// 起動時の初期化（前回状態の復元 → 組織一覧）。
    /// 組織一覧はキャッシュから即時復元し、最新化はバックグラウンドで行う（キャッシュがない初回のみ従来どおり待機）。
    /// </summary>
    public async Task InitializeAsync()
    {
        ReloadRecentFolders();
        ReloadRecentUrls();

        var settings = _settings.Current;
        if (!string.IsNullOrWhiteSpace(settings.LastFolder))
        {
            SelectedFolder = settings.LastFolder;
        }

        var cached = _orgCache.Load();
        if (cached.Orgs.Count > 0)
        {
            ApplyOrgs(cached.Orgs);
            StatusMessage = UiText.T("Msg_OrgCountCachedFmt", Orgs.Count);
            _log.Info($"組織一覧をキャッシュから復元: {Orgs.Count} 件");
            StartBackgroundOrgsRefresh();
        }
        else
        {
            await RefreshOrgsAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshOrgsAsync()
    {
        if (IsLoadingOrgs)
        {
            StatusMessage = UiText.T("Msg_OrgsLoadingWait");
            return;
        }

        // 手動更新を優先するため、バックグラウンド更新は中断する
        _backgroundOrgsCts?.Cancel();

        IsLoadingOrgs = true;
        StatusMessage = UiText.T("Msg_LoadingOrgs");
        try
        {
            var orgs = await _orgService.ListOrgsAsync();
            ApplyOrgs(orgs);
            _orgCache.Save(orgs);
            StatusMessage = UiText.T("Msg_OrgCountFmt", Orgs.Count);
            _log.Info($"組織一覧を取得: {Orgs.Count} 件");
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Msg_OrgLoadFailedFmt", ex.Message);
            _log.Error("組織一覧の取得に失敗", ex);
        }
        finally
        {
            IsLoadingOrgs = false;
        }
    }

    /// <summary>組織一覧を Orgs へ反映し、選択中の組織（無ければ前回の組織 → 既定）を復元する。</summary>
    private void ApplyOrgs(IReadOnlyList<OrgInfo> orgs)
    {
        var previousUsername = SelectedOrg?.Username;
        var lastUsername = _settings.Current.LastOrgUsername;

        Orgs.Clear();
        foreach (var org in orgs.OrderByDescending(o => o.IsDefault).ThenBy(o => o.DisplayName, StringComparer.CurrentCulture))
        {
            Orgs.Add(org);
        }

        SelectedOrg = Orgs.FirstOrDefault(o => string.Equals(o.Username, previousUsername, StringComparison.OrdinalIgnoreCase))
                      ?? Orgs.FirstOrDefault(o => string.Equals(o.Username, lastUsername, StringComparison.OrdinalIgnoreCase))
                      ?? Orgs.FirstOrDefault(o => o.IsDefault)
                      ?? Orgs.FirstOrDefault();
    }

    /// <summary>
    /// キャッシュ表示後に組織一覧をバックグラウンドで更新する。
    /// UI は待たせず、sf の子プロセスは低優先度で実行する（画面操作の処理を優先）。
    /// </summary>
    private void StartBackgroundOrgsRefresh()
    {
        _backgroundOrgsCts?.Cancel();
        var cts = new CancellationTokenSource();
        _backgroundOrgsCts = cts;
        StatusMessage = UiText.T("Msg_OrgsRefreshing");
        _ = Task.Run(async () =>
        {
            try
            {
                var orgs = await _orgService.ListOrgsAsync(cts.Token, background: true);
                _ui.Invoke(() =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    ApplyOrgs(orgs);
                    _orgCache.Save(orgs);
                    StatusMessage = UiText.T("Msg_OrgCountFmt", Orgs.Count);
                    _log.Info($"組織一覧をバックグラウンド更新: {Orgs.Count} 件");
                });
            }
            catch (OperationCanceledException)
            {
                // 手動更新などに処理を譲った
            }
            catch (Exception ex)
            {
                _log.Warn($"組織一覧のバックグラウンド更新に失敗: {ex.Message}");
            }
        });
    }

    /// <summary>選択中組織のトークンをバックグラウンドで事前取得する（初回の SOQL 実行を速くする）。</summary>
    private void StartAuthPrefetch(OrgInfo? org)
    {
        var target = org?.Alias ?? org?.Username;
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _orgService.GetAuthAsync(target, background: true);
                _log.Info($"トークンを事前取得: {org!.DisplayName}");
            }
            catch (Exception ex)
            {
                _log.Warn($"トークンの事前取得に失敗: {ex.Message}");
            }
        });
    }

    /// <summary>補完候補用のメタデータ（オブジェクト一覧 / Apex クラス等）をバックグラウンドで温める。</summary>
    private void StartSuggestionPrefetch(OrgInfo? org)
    {
        var target = org?.Alias ?? org?.Username;
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // 画面操作（実行・入力）を優先するため、1 つずつ順番に温める
                await _describes.ListObjectsAsync(target, cancellationToken: CancellationToken.None);
                await _metadata.ListApexClassesAsync(target, cancellationToken: CancellationToken.None);
                await _metadata.ListCustomMetadataTypesAsync(target, cancellationToken: CancellationToken.None);
                _log.Info($"補完候補のメタデータを事前取得: {org!.DisplayName}");
            }
            catch (Exception ex)
            {
                _log.Warn($"補完候補のメタデータ事前取得に失敗: {ex.Message}");
            }
        });
    }

    partial void OnSelectedOrgChanged(OrgInfo? value)
    {
        // 各機能ビューへ対象組織（--target-org / REST 用）を伝播
        var target = value?.Alias ?? value?.Username;
        foreach (var tab in SoqlTabs.Tabs)
        {
            tab.CurrentOrg = target;
        }

        foreach (var tab in ApexTabs.Tabs)
        {
            tab.CurrentOrg = target;
        }

        foreach (var tab in CommandTabs.Tabs)
        {
            tab.CurrentOrg = target;
        }

        Ai.CurrentOrg = target;
        Logs.CurrentOrg = target;
        Api.CurrentOrg = target;
        Deploy.CurrentOrg = target;

        // 初回実行を速くするため、選択された組織のトークンをバックグラウンドで事前取得する
        StartAuthPrefetch(value);

        // 補完候補（オブジェクト / 項目 / Apex クラス）のメタデータをバックグラウンドで温める
        StartSuggestionPrefetch(value);

        if (value is null || string.Equals(_settings.Current.LastOrgUsername, value.Username, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.Current.LastOrgUsername = value.Username;
        _settings.Save();
    }

    partial void OnSelectedFolderChanged(string? value)
    {
        foreach (var tab in SoqlTabs.Tabs)
        {
            tab.CurrentFolder = value;
        }

        foreach (var tab in ApexTabs.Tabs)
        {
            tab.CurrentFolder = value;
        }

        foreach (var tab in CommandTabs.Tabs)
        {
            tab.CurrentFolder = value;
        }

        Deploy.CurrentFolder = value;
    }

    private void OnReplayRequested(HistoryEntry entry)
    {
        switch (entry.Type)
        {
            case HistoryTypes.Soql:
                Soql.LoadFromHistory(entry, autoRun: true);
                SelectedTabIndex = 0;
                StatusMessage = UiText.T("Msg_ReplaySoql");
                break;

            case HistoryTypes.Apex:
                Apex.LoadFromHistory(entry, autoRun: false);
                SelectedTabIndex = 1;
                StatusMessage = UiText.T("Msg_ReplayApex");
                break;

            case HistoryTypes.Command:
                Command.LoadFromHistory(entry, autoRun: false);
                SelectedTabIndex = 5;
                StatusMessage = UiText.T("Msg_ReplayCommand");
                break;

            case HistoryTypes.Api:
                Api.LoadFromHistory(entry, autoRun: false);
                SelectedTabIndex = 6;
                StatusMessage = UiText.T("Msg_ReplayApi");
                break;

            case HistoryTypes.Deploy:
                Deploy.LoadFromHistory(entry, autoRun: false);
                SelectedTabIndex = 4;
                StatusMessage = UiText.T("Msg_ReplayDeploy");
                break;

            case HistoryTypes.Data:
            {
                var org = Orgs.FirstOrDefault(o =>
                    string.Equals(o.Alias ?? o.Username, entry.Org, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(o.Username, entry.Org, StringComparison.OrdinalIgnoreCase));
                if (org is null)
                {
                    StatusMessage = UiText.T("Msg_ReplayUnsupportedFmt", entry.TypeLabel);
                    break;
                }

                SelectedOrg = org;
                // エクスポートの SOQL のみ復元する（インポートの Params は "|" 区切り）
                var soql = entry.Params is { Length: > 0 } p && !p.Contains('|') ? p : null;
                _windows.OpenDataIo(org, null, soql);
                StatusMessage = UiText.T("Msg_DataIoOpenedFmt", org.DisplayName);
                break;
            }

            default:
                StatusMessage = UiText.T("Msg_ReplayUnsupportedFmt", entry.TypeLabel);
                break;
        }
    }

    /// <summary>クイックパネル / Ctrl+1..9 からお気に入りを実行する。</summary>
    public void ExecuteFavorite(FavoriteItem favorite)
    {
        switch (favorite.Type.ToLowerInvariant())
        {
            case HistoryTypes.Soql:
                Soql.LoadFavorite(favorite, autoRun: true);
                SelectedTabIndex = 0;
                StatusMessage = UiText.T("Msg_FavoriteRunFmt", favorite.Label);
                break;

            case HistoryTypes.Apex:
                Apex.LoadFavorite(favorite, autoRun: false);
                SelectedTabIndex = 1;
                StatusMessage = UiText.T("Msg_FavoriteLoadedApexFmt", favorite.Label);
                break;

            case HistoryTypes.Command:
                Command.LoadFavorite(favorite, autoRun: false);
                SelectedTabIndex = 5;
                StatusMessage = UiText.T("Msg_FavoriteLoadedCommandFmt", favorite.Label);
                break;

            case HistoryTypes.Api:
                Api.LoadFavorite(favorite, autoRun: false);
                SelectedTabIndex = 6;
                StatusMessage = UiText.T("Msg_FavoriteLoadedApiFmt", favorite.Label);
                break;

            case HistoryTypes.Deploy:
                Deploy.LoadFavorite(favorite, autoRun: false);
                SelectedTabIndex = 4;
                StatusMessage = UiText.T("Msg_FavoriteLoadedDeployFmt", favorite.Label);
                break;

            case "url":
                LaunchUrl(favorite.Payload);
                break;

            case "folder":
                CommitFolder(favorite.Payload);
                break;

            default:
                StatusMessage = UiText.T("Msg_FavoriteUnsupportedFmt", favorite.Type);
                break;
        }
    }

    /// <summary>Ctrl+数字（1〜9）でクイックパネルのスロットを実行する。</summary>
    public void ExecuteQuickSlot(int number)
    {
        var favorite = QuickPanel.GetSlot(number);
        if (favorite is null)
        {
            StatusMessage = UiText.T("Msg_QuickSlotEmptyFmt", number);
            return;
        }

        ExecuteFavorite(favorite);
    }

    /// <summary>F5: 直前の操作を再実行する（種別ごとの安全な再実行）。</summary>
    public void ReplayLastOperation()
    {
        var last = _history.Query().FirstOrDefault();
        if (last is null)
        {
            StatusMessage = UiText.T("Msg_NoHistory");
            return;
        }

        OnReplayRequested(last);
    }

    /// <summary>AI が生成したコード片を該当タブへ読み込む。</summary>
    private void ApplyAiSnippet(AiSnippet snippet)
    {
        switch (snippet.Language)
        {
            case "soql":
                Soql.LoadText(snippet.Code);
                SelectedTabIndex = 0;
                StatusMessage = UiText.T("Ai_AppliedSoql");
                break;

            case "apex":
                Apex.LoadText(snippet.Code);
                SelectedTabIndex = 1;
                StatusMessage = UiText.T("Ai_AppliedApex");
                break;

            case "command":
                Command.LoadText(snippet.Code);
                SelectedTabIndex = 5;
                StatusMessage = UiText.T("Ai_AppliedCommand");
                break;
        }
    }

    /// <summary>選択中組織の組織情報ウィンドウを新しいウィンドウで開く。</summary>
    [RelayCommand]
    private void OpenOrgInfo()
    {
        if (IsLoadingOrgs)
        {
            StatusMessage = UiText.T("Msg_OrgsLoadingWait");
            return;
        }

        if (SelectedOrg is null)
        {
            StatusMessage = UiText.T("Msg_SelectOrg");
            return;
        }

        try
        {
            _windows.OpenOrgInfo(SelectedOrg);
            StatusMessage = UiText.T("Msg_OrgInfoOpenedFmt", SelectedOrg.DisplayName);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("組織情報ウィンドウを開けませんでした", ex);
        }
    }

    /// <summary>複数組織の比較ウィンドウを新しいウィンドウで開く。</summary>
    [RelayCommand]
    private void OpenCompareOrgs()
    {
        if (IsLoadingOrgs)
        {
            StatusMessage = UiText.T("Msg_OrgsLoadingWait");
            return;
        }

        if (Orgs.Count < 2)
        {
            StatusMessage = UiText.T("Compare_NeedTwoOrgs");
            return;
        }

        try
        {
            _windows.OpenCompareOrgs(Orgs.ToList());
            StatusMessage = UiText.T("Msg_CompareOpenedFmt");
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("組織比較ウィンドウを開けませんでした", ex);
        }
    }

    /// <summary>データ入出力ウィンドウを新しいウィンドウで開く。</summary>
    [RelayCommand]
    private void OpenDataIo()
    {
        if (IsLoadingOrgs)
        {
            StatusMessage = UiText.T("Msg_OrgsLoadingWait");
            return;
        }

        if (SelectedOrg is null)
        {
            StatusMessage = UiText.T("Msg_SelectOrg");
            return;
        }

        try
        {
            _windows.OpenDataIo(SelectedOrg);
            StatusMessage = UiText.T("Msg_DataIoOpenedFmt", SelectedOrg.DisplayName);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("データ入出力ウィンドウを開けませんでした", ex);
        }
    }

    /// <summary>バックアップと復元ウィンドウを新しいウィンドウで開く。</summary>
    [RelayCommand]
    private void OpenBackup()
    {
        if (IsLoadingOrgs)
        {
            StatusMessage = UiText.T("Msg_OrgsLoadingWait");
            return;
        }

        if (SelectedOrg is null)
        {
            StatusMessage = UiText.T("Msg_SelectOrg");
            return;
        }

        try
        {
            _windows.OpenBackup(SelectedOrg);
            StatusMessage = UiText.T("Msg_BackupOpenedFmt", SelectedOrg.DisplayName);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("バックアップ ウィンドウを開けませんでした", ex);
        }
    }

    /// <summary>組織管理ウィンドウを新しいウィンドウで開く（組織未選択でも可）。</summary>
    [RelayCommand]
    private void OpenOrgManage()
    {
        try
        {
            _windows.OpenOrgManage(SelectedOrg);
            StatusMessage = UiText.T("Msg_OrgManageOpenedFmt", SelectedOrg?.DisplayName ?? UiText.T("Main_NotSelected"));
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("組織管理ウィンドウを開けませんでした", ex);
        }
    }

    /// <summary>スクラッチ組織ウィンドウを新しいウィンドウで開く（組織未選択でも可）。</summary>
    [RelayCommand]
    private void OpenScratchOrg()
    {
        try
        {
            _windows.OpenScratchOrg(SelectedOrg);
            StatusMessage = UiText.T("Msg_ScratchOpenedFmt", SelectedOrg?.DisplayName ?? UiText.T("Main_NotSelected"));
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("スクラッチ組織ウィンドウを開けませんでした", ex);
        }
    }

    /// <summary>ETL 移行ウィンドウを新しいウィンドウで開く（組織未選択でも可）。</summary>
    [RelayCommand]
    private void OpenEtl()
    {
        try
        {
            _windows.OpenEtl(SelectedOrg);
            StatusMessage = UiText.T("Msg_EtlOpenedFmt", SelectedOrg?.DisplayName ?? UiText.T("Main_NotSelected"));
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("ETL ウィンドウを開けませんでした", ex);
        }
    }

    /// <summary>ソース エディタ ウィンドウを新しいウィンドウで開く（組織を選択している場合のみ）。</summary>
    [RelayCommand]
    private void OpenSourceEditor()
    {
        if (IsLoadingOrgs)
        {
            StatusMessage = UiText.T("Msg_OrgsLoadingWait");
            return;
        }

        if (SelectedOrg is null)
        {
            StatusMessage = UiText.T("Msg_SelectOrg");
            return;
        }

        try
        {
            _windows.OpenSourceEditor(SelectedOrg);
            StatusMessage = UiText.T("Msg_SourceEditorOpenedFmt", SelectedOrg.DisplayName);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("ソース エディタ ウィンドウを開けませんでした", ex);
        }
    }

    /// <summary>設定タブのインデックス（ようこそ画面の「設定を開く」遷移先）。</summary>
    private const int SettingsTabIndex = 7;

    /// <summary>ようこそ画面をモーダルで表示する（起動時の自動表示と設定タブのボタンから呼ばれる）。</summary>
    public void ShowWelcome()
    {
        try
        {
            _windows.OpenWelcome(() => SelectedTabIndex = SettingsTabIndex);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("ようこそ画面を開けませんでした", ex);
        }
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        var initialDirectory = !string.IsNullOrWhiteSpace(SelectedFolder) && Directory.Exists(SelectedFolder) ? SelectedFolder : null;
        var folder = await _filePicker.PickFolderAsync(UiText.T("Dlg_BrowseFolderTitle"), initialDirectory);
        if (folder is not null)
        {
            CommitFolder(folder);
        }
    }

    /// <summary>フォルダを確定する（最近使用リストへの記録 + 設定保存）。</summary>
    public void CommitFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        path = path.Trim();
        if (!Directory.Exists(path))
        {
            StatusMessage = UiText.T("Msg_FolderNotExistFmt", path);
            return;
        }

        SelectedFolder = path;
        _recentFolders.Touch(path);
        _settings.Current.LastFolder = path;
        _settings.Save();
        ReloadRecentFolders();
        StatusMessage = UiText.T("Msg_FolderSetFmt", path);
    }

    // ---- ツールランチャー ----

    /// <summary>ターミナルを起動（kind: wt / powershell / cmd / wsl）。</summary>
    [RelayCommand]
    private void OpenTerminal(string? kind)
    {
        var folder = ResolveLaunchFolder();
        var result = _toolLauncher.LaunchTerminal(folder, kind ?? "wt");
        StatusMessage = result.Success ? UiText.T("Msg_TerminalFmt", folder) : result.Message;

        if (result.Success)
        {
            _log.Info($"ターミナル起動 ({kind ?? "wt"}): {folder}");
        }
        else
        {
            _log.Warn(result.Message);
        }
    }

    /// <summary>エクスプローラーで開く（mode=copy-path でパスをコピー）。</summary>
    [RelayCommand]
    private void OpenExplorer(string? mode)
    {
        if (mode == "copy-path")
        {
            try
            {
                _clipboard.TrySetText(ResolveLaunchFolder());
                StatusMessage = UiText.T("Msg_Copied");
            }
            catch (Exception ex)
            {
                StatusMessage = UiText.T("Msg_CopyFailedFmt", ex.Message);
            }

            return;
        }

        var folder = ResolveLaunchFolder();
        var result = _toolLauncher.LaunchExplorer(folder);
        StatusMessage = result.Success ? UiText.T("Msg_ExplorerFmt", folder) : result.Message;
    }

    /// <summary>VS Code で開く（mode: new / reuse）。</summary>
    [RelayCommand]
    private void OpenVsCode(string? mode)
    {
        var folder = ResolveLaunchFolder();
        var result = _toolLauncher.LaunchVsCode(folder, mode ?? "new");
        StatusMessage = result.Success ? UiText.T("Msg_VsCodeFmt", folder) : result.Message;
    }

    /// <summary>ブラウザで開く（target: org-home / org-setup / login / input / url:...）。</summary>
    [RelayCommand]
    private async Task OpenBrowserAsync(string? target)
    {
        target ??= "org-home";
        try
        {
            switch (target)
            {
                case "input":
                    var input = _dialogs.Prompt(
                        UiText.T("Msg_InputUrlTitle"),
                        UiText.T("Msg_InputUrlPrompt"));
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        LaunchUrl(input.Trim());
                    }

                    break;

                case "login":
                    LaunchUrl("https://login.salesforce.com");
                    break;

                case "org-home":
                case "org-setup":
                    if (IsLoadingOrgs)
                    {
                        StatusMessage = UiText.T("Msg_OrgsLoadingWait");
                        return;
                    }

                    var org = Soql.CurrentOrg;
                    if (string.IsNullOrWhiteSpace(org))
                    {
                        StatusMessage = UiText.T("Msg_SelectOrg");
                        return;
                    }

                    StatusMessage = UiText.T("Msg_GetOrgUrl");
                    var path = target == "org-setup" ? "/lightning/setup/SetupOneHome/home" : "/lightning/page/home";

                    // セッション付き URL（frontdoor・ログイン不要）を優先。取得できない場合は通常 URL にフォールバック。
                    // セッション URL は認証情報を含むため「最近の URL」には保存しない。
                    var frontDoor = await _orgService.GetFrontDoorUrlAsync(org, path);
                    if (!string.IsNullOrWhiteSpace(frontDoor))
                    {
                        LaunchUrl(frontDoor, persist: false);
                        return;
                    }

                    var auth = await _orgService.GetAuthAsync(org);
                    if (string.IsNullOrWhiteSpace(auth.InstanceUrl))
                    {
                        StatusMessage = UiText.T("Msg_NoInstanceUrl");
                        return;
                    }

                    LaunchUrl(auth.InstanceUrl.TrimEnd('/') + path);
                    break;

                default:
                    if (target.StartsWith("url:", StringComparison.OrdinalIgnoreCase))
                    {
                        LaunchUrl(target[4..]);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Msg_BrowserFailedFmt", ex.Message);
            _log.Error("ブラウザ起動に失敗", ex);
        }
    }

    /// <summary>最近の URL を開く。</summary>
    [RelayCommand]
    private void OpenRecentUrl(RecentUrl? item)
    {
        if (item is not null)
        {
            LaunchUrl(item.Url);
        }
    }

    private void LaunchUrl(string url, bool persist = true)
    {
        var result = _toolLauncher.LaunchBrowser(url);
        if (result.Success)
        {
            if (persist)
            {
                _recentUrls.Touch(url);
                ReloadRecentUrls();
            }

            StatusMessage = UiText.T("Msg_BrowserFmt", url);
        }
        else
        {
            StatusMessage = result.Message;
        }
    }

    /// <summary>起動に使うフォルダ（未選択ならユーザープロファイル）。</summary>
    private string ResolveLaunchFolder()
    {
        if (!string.IsNullOrWhiteSpace(SelectedFolder) && Directory.Exists(SelectedFolder))
        {
            return SelectedFolder;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private void ReloadRecentFolders()
    {
        RecentFolders.Clear();
        foreach (var folder in _recentFolders.GetOrdered())
        {
            RecentFolders.Add(folder);
        }
    }

    private void ReloadRecentUrls()
    {
        RecentUrls.Clear();
        foreach (var url in _recentUrls.GetOrdered().Take(10))
        {
            RecentUrls.Add(url);
        }
    }

    /// <summary>言語切替時に計算文字列（ヒント・ステータス詳細）を再通知する。</summary>
    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(OrgHintText));
        OnPropertyChanged(nameof(HasOrgHint));
        OnPropertyChanged(nameof(StatusDetail));
        SoqlTabs.RefreshTitles();
        ApexTabs.RefreshTitles();
        CommandTabs.RefreshTitles();
        Api.RefreshTree();
    }
}
