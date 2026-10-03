using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.App.Services;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// 組織情報ウィンドウ 1 つ分の ViewModel（ウィンドウごとに新規作成）。
/// 初回（キャッシュなし）のみ自動取得し、以降は手動の再取得まで API を呼ばない。
/// </summary>
public partial class OrgInfoViewModel : ObservableObject, IDisposable
{
    private readonly OrgInfoService _service;
    private readonly OrgInfoCacheStore _cache;
    private readonly OrgInfoSearchService _search;
    private readonly AppLog _log;
    private readonly DispatcherTimer _searchTimer;
    private readonly ToolLauncherService _toolLauncher;
    private readonly AiChatViewModel _ai;
    private readonly OrgInfoPreferencesStore _preferences;
    private readonly DataIoWindowFactory _dataIoFactory;
    private readonly List<OrgInfoCustomTabViewModel> _customTabs = new();

    private string _orgKey = string.Empty;
    private bool _initialized;
    private bool _loaded;
    private bool _autoFetchNeeded;
    private bool _suppressPreferencesReload;
    private OrgInfoMySettingsViewModel? _mySettings;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private OrgInfo? _org;

    [ObservableProperty]
    private string _title = UiText.T("OrgInfo_TitleFmt", string.Empty);

    [ObservableProperty]
    private string _orgDetail = string.Empty;

    [ObservableProperty]
    private string _statusMessage = UiText.T("Common_Ready");

    [ObservableProperty]
    private string _lastFetchedText = UiText.T("OrgInfo_NotFetched");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    private bool _isBusy;

    [ObservableProperty]
    private IOrgInfoTab? _selectedSection;

    [ObservableProperty]
    private string _globalSearchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchPanelOpen;

    [ObservableProperty]
    private string _searchStatusText = string.Empty;

    /// <summary>AI パネルの表示状態（ウィンドウ単位で独立した会話）。</summary>
    [ObservableProperty]
    private bool _isAiPanelOpen;

    /// <summary>このウィンドウ専用の AI チャット（メインウィンドウとは独立）。</summary>
    public AiChatViewModel Ai => _ai;

    /// <summary>組織情報タブ（固定セクション + オブジェクト項目）。</summary>
    public ObservableCollection<IOrgInfoTab> Sections { get; } = new();

    /// <summary>オブジェクト項目タブ（遅延取得）。</summary>
    public OrgInfoFieldsViewModel? Fields { get; private set; }

    /// <summary>全タブ横断検索の結果。</summary>
    public ObservableCollection<OrgInfoSearchHit> SearchResults { get; } = new();

    public bool CanRefresh => !IsBusy;

    public OrgInfoViewModel(OrgInfoService service, OrgInfoCacheStore cache, OrgInfoSearchService search, ToolLauncherService toolLauncher, AiChatViewModel ai, OrgInfoPreferencesStore preferences, DataIoWindowFactory dataIoFactory, AppLog log)
    {
        _service = service;
        _cache = cache;
        _search = search;
        _toolLauncher = toolLauncher;
        _ai = ai;
        _preferences = preferences;
        _dataIoFactory = dataIoFactory;
        _log = log;
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            RunGlobalSearch();
        };
    }

    /// <summary>対象組織を設定してセクションを作成し、キャッシュを読み込む（取得はしない）。</summary>
    public void Initialize(OrgInfo org)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Org = org;
        _orgKey = OrgInfoCacheStore.GetOrgKey(org);
        Title = UiText.T("OrgInfo_TitleFmt", org.DisplayName);
        OrgDetail = string.IsNullOrWhiteSpace(org.OrgId) ? org.DisplayName : $"{org.DisplayName} / {org.OrgId}";

        Sections.Clear();
        foreach (var definition in OrgInfoSections.All)
        {
            var section = new OrgInfoSectionViewModel(
                definition.Id,
                definition.TitleKey,
                OrgInfoSections.ColumnsFor(definition.Id),
                org,
                _orgKey,
                _service,
                _cache,
                _log,
                _toolLauncher,
                OrgInfoUrlBuilder.ForSection(org.InstanceUrl, definition.Id));
            section.Fetched += OnSectionFetched;
            if (definition.Id == OrgInfoSections.Objects)
            {
                section.DataIoRequested += OnDataIoRequested;
            }

            Sections.Add(section);
        }

        Fields = new OrgInfoFieldsViewModel(org, _orgKey, _service, _cache, _log, _toolLauncher);
        Sections.Add(Fields);

        // マイ設定（カスタムタブ + 管理タブ。タブ順: 固定 → 項目 → カスタム → マイ設定）
        _mySettings = new OrgInfoMySettingsViewModel();
        _mySettings.CreateRequested += OnCustomTabCreateRequested;
        _mySettings.DeleteRequested += OnCustomTabDeleteRequested;
        _mySettings.SaveRequested += OnCustomTabSaveRequested;
        LoadCustomTabs();
        Sections.Add(_mySettings);
        _mySettings.SetTabs(_customTabs);
        _preferences.PreferencesUpdated += OnPreferencesUpdated;

        // AI パネル（ウィンドウ単位の独立会話 + 表示中タブのデータ添付）
        _ai.CurrentOrg = org.DisplayName;
        _ai.ExtraSystemContextProvider = BuildAiContext;
        _ai.TabDataProvider = BuildTabAttachment;
        RefreshQuickPrompts();
        _ai.ShowTabDataButton = true;

        SelectedSection = Sections.FirstOrDefault();

        var cache = _cache.Load(_orgKey);
        var loadedCount = 0;
        foreach (var section in Sections.OfType<OrgInfoSectionViewModel>())
        {
            if (cache.Sections.TryGetValue(section.Id, out var data))
            {
                section.Apply(data);
                loadedCount++;
            }
        }

        UpdateLastFetched();
        _autoFetchNeeded = Sections.OfType<OrgInfoSectionViewModel>().All(s => !s.HasData);
        StatusMessage = loadedCount > 0
            ? UiText.T("OrgInfo_StatusLoadedFmt", loadedCount)
            : UiText.T("OrgInfo_NotFetched");

        _cache.SectionUpdated += OnSectionUpdated;
        UiText.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>オブジェクトタブからデータ入出力ウィンドウを開く（選択中オブジェクトをプリセット）。</summary>
    private void OnDataIoRequested(string? objectApiName)
    {
        if (Org is null)
        {
            return;
        }

        try
        {
            _dataIoFactory.Open(Org, objectApiName, null, Application.Current?.MainWindow);
            StatusMessage = UiText.T("Msg_DataIoOpenedFmt", Org.DisplayName);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("データ入出力ウィンドウを開けませんでした", ex);
        }
    }

    /// <summary>ウィンドウ表示時に呼ぶ。キャッシュが無い初回のみ自動取得する。</summary>
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!_autoFetchNeeded)
        {
            return;
        }

        StatusMessage = UiText.T("OrgInfo_FirstLoad");
        await RefreshSectionsAsync(Sections.OfType<OrgInfoSectionViewModel>().ToList(), firstLoad: true);
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        var sections = Sections.OfType<OrgInfoSectionViewModel>().ToList();
        var targets = sections.Where(s => s.HasData).ToList();
        if (targets.Count == 0)
        {
            targets = sections;
        }

        await RefreshSectionsAsync(targets, firstLoad: false);
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ToggleAiPanel() => IsAiPanelOpen = !IsAiPanelOpen;

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _searchTimer.Stop();
        _cache.SectionUpdated -= OnSectionUpdated;
        _preferences.PreferencesUpdated -= OnPreferencesUpdated;
        UiText.LanguageChanged -= OnLanguageChanged;
        if (_mySettings is not null)
        {
            _mySettings.CreateRequested -= OnCustomTabCreateRequested;
            _mySettings.DeleteRequested -= OnCustomTabDeleteRequested;
            _mySettings.SaveRequested -= OnCustomTabSaveRequested;
        }

        foreach (var tab in _customTabs)
        {
            tab.EditRequested -= OnCustomTabEditRequested;
        }

        _customTabs.Clear();
        foreach (var section in Sections)
        {
            if (section is OrgInfoSectionViewModel fixedSection)
            {
                fixedSection.Fetched -= OnSectionFetched;
            }

            section.Dispose();
        }
    }

    private async Task RefreshSectionsAsync(IReadOnlyList<OrgInfoSectionViewModel> targets, bool firstLoad)
    {
        if (IsBusy || targets.Count == 0)
        {
            return;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var succeeded = 0;
        var canceled = false;
        try
        {
            _cache.UpdateOrgMetadata(_orgKey, Org!);
            for (var index = 0; index < targets.Count; index++)
            {
                if (_cts.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                var section = targets[index];
                StatusMessage = UiText.T("OrgInfo_StatusLoadingFmt", section.Title, index + 1, targets.Count);
                if (await section.FetchAsync(_cts.Token))
                {
                    succeeded++;
                }

                if (_cts.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }
            }
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
            UpdateLastFetched();
        }

        StatusMessage = canceled
            ? UiText.T("Common_Canceled")
            : firstLoad && succeeded == targets.Count
                ? UiText.T("OrgInfo_StatusLoadedFmt", succeeded)
                : UiText.T("OrgInfo_StatusRefreshedFmt", succeeded);
    }

    private void OnSectionFetched(OrgInfoSectionViewModel section)
    {
        UpdateLastFetched();
        if (!IsBusy)
        {
            StatusMessage = UiText.T("OrgInfo_StatusRefreshedFmt", section.Title);
        }
    }

    /// <summary>他ウィンドウがキャッシュを更新したときに表示へ反映する（自分の取得中は自前で反映済みのため除外）。</summary>
    private void OnSectionUpdated(string orgKey, string sectionId)
    {
        if (!string.Equals(orgKey, _orgKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var section = Sections.OfType<OrgInfoSectionViewModel>()
            .FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.Ordinal));
        if (section is not null && !section.IsLoading)
        {
            var data = _cache.GetSection(_orgKey, sectionId);
            if (data is not null)
            {
                section.Apply(data);
                UpdateLastFetched();
            }
        }
        else if (OrgInfoSections.IsFieldsSection(sectionId))
        {
            Fields?.HandleSectionUpdated(sectionId);
        }

        if (string.Equals(sectionId, OrgInfoSections.Objects, StringComparison.Ordinal))
        {
            Fields?.ReloadObjectCandidates();
        }
    }

    private void OnLanguageChanged()
    {
        Title = UiText.T("OrgInfo_TitleFmt", Org?.DisplayName ?? string.Empty);
        foreach (var section in Sections)
        {
            section.Relocalize();
        }

        RefreshQuickPrompts();
        if (IsSearchPanelOpen)
        {
            RunGlobalSearch();
        }
    }

    // ---- マイ設定（カスタムタブ）----

    /// <summary>保存済みのマイ設定からカスタムタブを作り直す（外部更新時も使う）。</summary>
    private void LoadCustomTabs()
    {
        foreach (var tab in _customTabs)
        {
            Sections.Remove(tab);
            tab.EditRequested -= OnCustomTabEditRequested;
            tab.Dispose();
        }

        _customTabs.Clear();
        var prefs = _preferences.Load(_orgKey);
        foreach (var definition in prefs.Tabs)
        {
            var tab = new OrgInfoCustomTabViewModel(definition.Id, definition.Name, definition.Items, Org!, _orgKey, _service, _cache, _log, _toolLauncher);
            tab.EditRequested += OnCustomTabEditRequested;
            _customTabs.Add(tab);
            var index = _mySettings is null ? -1 : Sections.IndexOf(_mySettings);
            if (index >= 0)
            {
                Sections.Insert(index, tab);
            }
            else
            {
                Sections.Add(tab);
            }
        }
    }

    private void OnCustomTabCreateRequested()
    {
        if (_mySettings is null || Org is null)
        {
            return;
        }

        var id = Guid.NewGuid().ToString("N");
        var name = UiText.T("OrgInfo_Custom_DefaultNameFmt", _customTabs.Count + 1);
        var tab = new OrgInfoCustomTabViewModel(id, name, Array.Empty<string>(), Org, _orgKey, _service, _cache, _log, _toolLauncher);
        tab.EditRequested += OnCustomTabEditRequested;
        _customTabs.Add(tab);
        var index = Sections.IndexOf(_mySettings);
        if (index >= 0)
        {
            Sections.Insert(index, tab);
        }
        else
        {
            Sections.Add(tab);
        }

        PersistCustomTabs();
        _mySettings.SetTabs(_customTabs, selectId: id);
        SelectedSection = tab;
    }

    private void OnCustomTabDeleteRequested(string tabId)
    {
        var tab = _customTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is null)
        {
            return;
        }

        Sections.Remove(tab);
        _customTabs.Remove(tab);
        tab.EditRequested -= OnCustomTabEditRequested;
        tab.Dispose();
        PersistCustomTabs();
        _mySettings?.SetTabs(_customTabs);
        if (ReferenceEquals(SelectedSection, tab))
        {
            SelectedSection = _mySettings ?? Sections.FirstOrDefault();
        }
    }

    private void OnCustomTabSaveRequested(string tabId, string name, IReadOnlyList<string> itemIds)
    {
        var tab = _customTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is null)
        {
            return;
        }

        tab.Update(name, itemIds);
        PersistCustomTabs();
        _mySettings?.NotifySaved();
    }

    private void OnCustomTabEditRequested(OrgInfoCustomTabViewModel tab)
    {
        if (_mySettings is null)
        {
            return;
        }

        SelectedSection = _mySettings;
        _mySettings.SelectTab(tab.Id);
    }

    private void PersistCustomTabs()
    {
        var prefs = new OrgInfoOrgPreferences
        {
            Tabs = _customTabs
                .Select(t => new OrgInfoCustomTab { Id = t.Id, Name = t.Name, Items = t.ItemIds.ToList() })
                .ToList(),
        };

        _suppressPreferencesReload = true;
        try
        {
            _preferences.Save(_orgKey, prefs);
        }
        finally
        {
            _suppressPreferencesReload = false;
        }
    }

    /// <summary>他ウィンドウがマイ設定を変更したときに取り込む（自分の保存は除外）。</summary>
    private void OnPreferencesUpdated(string orgKey)
    {
        if (!string.Equals(orgKey, _orgKey, StringComparison.OrdinalIgnoreCase) || _suppressPreferencesReload)
        {
            return;
        }

        LoadCustomTabs();
        _mySettings?.SetTabs(_customTabs);
    }

    // ---- AI 連携 ----

    private void RefreshQuickPrompts()
    {
        _ai.QuickPrompts.Clear();
        foreach (var key in new[] { "OrgInfo_Ai_Prompt_Summarize", "OrgInfo_Ai_Prompt_Risks", "OrgInfo_Ai_Prompt_UnusedPs", "OrgInfo_Ai_Prompt_Owd" })
        {
            _ai.QuickPrompts.Add(UiText.T(key));
        }

        _ai.HasQuickPrompts = _ai.QuickPrompts.Count > 0;
    }

    /// <summary>システムプロンプトへ追記する組織コンテキスト（組織名・ID・種別・インスタンス・表示中タブ）。</summary>
    private string? BuildAiContext()
    {
        if (Org is null)
        {
            return null;
        }

        var kind = Org.IsSandbox ? UiText.T("OrgInfo_Value_Sandbox") : UiText.T("OrgInfo_Value_Production");
        return UiText.T(
            "OrgInfo_Ai_ContextFmt",
            Org.DisplayName,
            Org.OrgId ?? "-",
            kind,
            Org.InstanceUrl ?? "-",
            SelectedSection?.Title ?? "-");
    }

    /// <summary>「表示中タブのデータを添付」で使う（タブ名, テキスト）。未取得・管理タブは null。</summary>
    private (string Name, string Text)? BuildTabAttachment()
    {
        switch (SelectedSection)
        {
            case OrgInfoSectionViewModel section:
                return section.BuildAttachmentText(OrgInfoAttachment.DefaultMaxChars) is { } sectionText
                    ? (section.Title, sectionText)
                    : null;
            case OrgInfoFieldsViewModel fields:
                return fields.BuildAttachmentText(OrgInfoAttachment.DefaultMaxChars) is { } fieldsText ? (fields.Title, fieldsText) : null;
            case OrgInfoCustomTabViewModel custom:
                return custom.BuildAttachmentText(OrgInfoAttachment.DefaultMaxChars) is { } customText ? (custom.Title, customText) : null;
            default:
                return null;
        }
    }

    partial void OnGlobalSearchTextChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    /// <summary>検索を即時実行する（Enter キー用）。</summary>
    public void RunGlobalSearchNow()
    {
        _searchTimer.Stop();
        RunGlobalSearch();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchTimer.Stop();
        GlobalSearchText = string.Empty;
        SearchResults.Clear();
        IsSearchPanelOpen = false;
        SearchStatusText = string.Empty;
    }

    /// <summary>検索結果の行へ移動する（結果グリッドのボタン用）。</summary>
    [RelayCommand]
    private void GoToHit(OrgInfoSearchHit? hit) => ActivateSearchHit(hit);

    /// <summary>検索結果から該当タブ・行へジャンプする。</summary>
    public void ActivateSearchHit(OrgInfoSearchHit? hit)
    {
        if (hit is null)
        {
            return;
        }

        if (OrgInfoSections.IsFieldsSection(hit.SectionId))
        {
            Fields?.ActivateSection(hit.SectionId);
            if (Fields is not null)
            {
                SelectedSection = Fields;
                Fields.Section?.SelectRow(hit.RowId);
            }

            return;
        }

        var section = Sections.OfType<OrgInfoSectionViewModel>()
            .FirstOrDefault(s => string.Equals(s.Id, hit.SectionId, StringComparison.Ordinal));
        if (section is null)
        {
            return;
        }

        SelectedSection = section;
        section.SelectRow(hit.RowId);
    }

    private void RunGlobalSearch()
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(GlobalSearchText))
        {
            IsSearchPanelOpen = false;
            SearchStatusText = string.Empty;
            return;
        }

        var sourceSections = Sections
            .OfType<OrgInfoSectionViewModel>()
            .Select(s => s.Source)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
        if (Fields?.Section?.Source is { } fieldsSection)
        {
            sourceSections.Add(fieldsSection);
        }

        foreach (var hit in _search.Search(sourceSections, GlobalSearchText))
        {
            SearchResults.Add(hit);
        }

        SearchStatusText = SearchResults.Count == 0
            ? UiText.T("OrgInfo_Search_NoMatch")
            : UiText.T("OrgInfo_Search_ResultCountFmt", SearchResults.Count);
        IsSearchPanelOpen = true;
    }

    private void UpdateLastFetched()
    {
        var values = Sections
            .Select(s => s.FetchedAt)
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .ToList();

        LastFetchedText = values.Count == 0
            ? UiText.T("OrgInfo_NotFetched")
            : UiText.T("OrgInfo_FetchedAtFmt", OrgInfoSectionViewModel.FormatTimestamp(values.Max()));
    }
}
