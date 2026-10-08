using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>組織比較ウィンドウの ViewModel（複数組織のグリッド比較・差分フィルタ・CSV 出力）。</summary>
public partial class CompareOrgsViewModel : ObservableObject, IDisposable
{
    private readonly OrgCompareService _compare;
    private readonly OrgRecordCompareService _recordCompare;
    private readonly OrgCompareStateStore _state;
    private readonly IFilePickerService _filePicker;
    private readonly ToolLauncherService _toolLauncher;
    private readonly IAppWindowService _appWindows;
    private readonly AppLog _log;
    private readonly List<OrgInfo> _allOrgs = new();
    private readonly List<CompareObjectCandidate> _objectCandidates = new();
    private CompareFieldsCategoryViewModel? _fieldsTab;
    private CompareRecordsCategoryViewModel? _recordsTab;
    private bool _candidatesLoading;
    private bool _suspendRefresh;
    private bool _disposed;

    public CompareOrgsViewModel(OrgCompareService compare, OrgRecordCompareService recordCompare, OrgCompareStateStore state, IFilePickerService filePicker, ToolLauncherService toolLauncher, IAppWindowService appWindows, AppLog log)
    {
        _compare = compare;
        _recordCompare = recordCompare;
        _state = state;
        _filePicker = filePicker;
        _toolLauncher = toolLauncher;
        _appWindows = appWindows;
        _log = log;
        _title = UiText.T("Compare_Title");

        foreach (var category in OrgCompareCategories.All)
        {
            var item = new CompareCategoryViewModel(category);
            item.OpenLinkRequested += OnOpenLinkRequested;
            Categories.Add(item);
        }

        UiText.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>組織一覧（チェックボックス）。</summary>
    public ObservableCollection<CompareOrgItemViewModel> Orgs { get; } = new();

    /// <summary>比較カテゴリ（タブ）。</summary>
    public ObservableCollection<CompareCategoryViewModel> Categories { get; } = new();

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _diffOnly;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private CompareCategoryViewModel? _selectedCategory;

    /// <summary>操作（再取得・CSV）が可能か。</summary>
    public bool CanInteract => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanInteract));

    /// <summary>メインウィンドウの組織一覧スナップショットで初期化する（永続状態を復元）。</summary>
    public void Initialize(IReadOnlyList<OrgInfo> orgs)
    {
        _allOrgs.Clear();
        _allOrgs.AddRange(orgs);
        var state = _state.Load();

        _suspendRefresh = true;
        try
        {
            Orgs.Clear();
            foreach (var org in _allOrgs)
            {
                var item = new CompareOrgItemViewModel(org)
                {
                    IsSelected = state.OrgUsernames.Contains(org.Username, StringComparer.OrdinalIgnoreCase),
                };
                item.PropertyChanged += OnOrgItemPropertyChanged;
                Orgs.Add(item);
            }

            // 選択が 2 件未満なら既定組織 + 先頭組織で補完する
            if (Orgs.Count(o => o.IsSelected) < 2)
            {
                var defaults = _allOrgs
                    .Where(o => o.IsDefault)
                    .Take(1)
                    .Concat(_allOrgs)
                    .Distinct()
                    .Take(2)
                    .Select(o => o.Username)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var item in Orgs)
                {
                    item.IsSelected = defaults.Contains(item.Org.Username);
                }
            }

            DiffOnly = state.DiffOnly;
            CreateFieldsTab(state.FieldsObject);
            CreateRecordsTab(state.RecordObject, state.RecordKeyField, state.RecordFields, state.RecordLimit);
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == state.CategoryId) ?? Categories.FirstOrDefault();
        }
        finally
        {
            _suspendRefresh = false;
        }
    }

    /// <summary>ウィンドウ表示時の初期ロード（現在のカテゴリをキャッシュ優先で構築）。</summary>
    public Task LoadAsync()
    {
        var category = SelectedCategory;
        _ = EnsureObjectCandidatesAsync();
        return category is null ? Task.CompletedTask : LoadCategoryAsync(category, forceRefresh: false, fetchMissing: true);
    }

    partial void OnSelectedCategoryChanged(CompareCategoryViewModel? value)
    {
        if (_suspendRefresh || _disposed || value is null || value.IsLoaded || value.IsLoading)
        {
            return;
        }

        if (value is CompareFieldsCategoryViewModel)
        {
            _ = EnsureObjectCandidatesAsync();
        }

        _ = LoadCategoryAsync(value, forceRefresh: false, fetchMissing: true);
    }

    partial void OnDiffOnlyChanged(bool value)
    {
        foreach (var category in Categories)
        {
            category.ApplyFilter(value);
        }

        PersistState();
    }

    /// <summary>現在のタブを強制再取得する。</summary>
    [RelayCommand]
    private Task RefreshCurrent()
    {
        var category = SelectedCategory;
        return category is null ? Task.CompletedTask : LoadCategoryAsync(category, forceRefresh: true, fetchMissing: true);
    }

    /// <summary>すべてのタブを順に強制再取得する。</summary>
    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        if (IsBusy)
        {
            return;
        }

        foreach (var category in Categories)
        {
            if (_disposed)
            {
                return;
            }

            await LoadCategoryAsync(category, forceRefresh: true, fetchMissing: true);
        }
    }

    /// <summary>セルの ↗ をブラウザーで開く。</summary>
    private void OnOpenLinkRequested(string url)
    {
        var result = _toolLauncher.LaunchBrowser(url);
        StatusMessage = result.Message;
    }

    /// <summary>「オブジェクト項目」タブを作り直す（動的カテゴリのため差し替え方式）。</summary>
    private void CreateFieldsTab(string? objectApiName)
    {
        var index = _fieldsTab is null ? Categories.Count : Math.Max(0, Categories.IndexOf(_fieldsTab));
        if (_fieldsTab is not null)
        {
            _fieldsTab.ObjectSelectionChanged -= OnFieldsObjectChanged;
            _fieldsTab.OpenLinkRequested -= OnOpenLinkRequested;
            Categories.Remove(_fieldsTab);
        }

        var tab = new CompareFieldsCategoryViewModel(objectApiName ?? string.Empty);
        tab.ObjectSelectionChanged += OnFieldsObjectChanged;
        tab.OpenLinkRequested += OnOpenLinkRequested;
        if (_objectCandidates.Count > 0)
        {
            tab.SetCandidates(_objectCandidates);
        }

        index = Math.Clamp(index, 0, Categories.Count);
        Categories.Insert(index, tab);
        _fieldsTab = tab;
    }

    /// <summary>オブジェクトが変わったらタブを作り直して選択・再比較する。</summary>
    private void OnFieldsObjectChanged(string objectApiName)
    {
        CreateFieldsTab(objectApiName);
        if (_fieldsTab is null)
        {
            return;
        }

        SelectedCategory = _fieldsTab;
        PersistState();
    }

    /// <summary>「レコード比較」タブを作り直す（動的カテゴリのため差し替え方式）。</summary>
    private void CreateRecordsTab(string? objectApiName, string? keyField = null, IReadOnlyList<string>? fields = null, int limit = 0)
    {
        var index = _recordsTab is null ? Categories.Count : Math.Max(0, Categories.IndexOf(_recordsTab));
        if (_recordsTab is not null)
        {
            _recordsTab.ObjectSelectionChanged -= OnRecordsObjectChanged;
            _recordsTab.OpenLinkRequested -= OnOpenLinkRequested;
            _recordsTab.RunRequested -= OnRecordsRunRequested;
            _recordsTab.DetailRequested -= OnRecordDetailRequested;
            Categories.Remove(_recordsTab);
        }

        var tab = new CompareRecordsCategoryViewModel(objectApiName ?? string.Empty, keyField, fields, limit);
        tab.ObjectSelectionChanged += OnRecordsObjectChanged;
        tab.OpenLinkRequested += OnOpenLinkRequested;
        tab.RunRequested += OnRecordsRunRequested;
        tab.DetailRequested += OnRecordDetailRequested;
        if (_objectCandidates.Count > 0)
        {
            tab.SetCandidates(_objectCandidates);
        }

        index = Math.Clamp(index, 0, Categories.Count);
        Categories.Insert(index, tab);
        _recordsTab = tab;
    }

    /// <summary>オブジェクトが変わったらタブを作り直して選択・再比較する（照合キー・比較項目はリセット、上限は維持）。</summary>
    private void OnRecordsObjectChanged(string objectApiName)
    {
        var limit = _recordsTab?.Limit ?? 0;
        CreateRecordsTab(objectApiName, null, null, limit);
        if (_recordsTab is null)
        {
            return;
        }

        SelectedCategory = _recordsTab;
        PersistState();
    }

    /// <summary>「比較実行」ボタン（条件変更後の再クエリ）。</summary>
    private void OnRecordsRunRequested()
    {
        PersistState();
        var tab = _recordsTab;
        if (tab is not null)
        {
            _ = LoadRecordsTabAsync(tab, forceRefresh: false);
        }
    }

    /// <summary>「詳細を表示」（行のダブルクリック含む）でレコード差分詳細ウィンドウを開く。</summary>
    private void OnRecordDetailRequested(CompareRowViewModel row)
    {
        var detail = _recordsTab?.BuildDetail(row);
        if (detail is not null)
        {
            _appWindows.OpenCompareRecordDetail(detail);
        }
    }

    /// <summary>「レコード比較」タブを実行する（候補確保 → 項目メタデータ → 各組織で REST SOQL → キー突合）。</summary>
    private async Task LoadRecordsTabAsync(CompareRecordsCategoryViewModel tab, bool forceRefresh)
    {
        if (IsBusy)
        {
            return;
        }

        await EnsureObjectCandidatesAsync();

        if (string.IsNullOrEmpty(tab.ObjectApiName))
        {
            tab.Clear();
            tab.EmptyMessage = UiText.T("Compare_RecordsSelectObject");
            StatusMessage = UiText.T("Compare_RecordsSelectObject");
            return;
        }

        var selected = Orgs.Where(o => o.IsSelected).Select(o => o.Org).ToList();
        if (selected.Count < 2)
        {
            tab.Clear();
            StatusMessage = UiText.T("Compare_NeedTwoOrgs");
            return;
        }

        IsBusy = true;
        tab.IsLoading = true;
        StatusMessage = UiText.T("Compare_Loading");
        try
        {
            await EnsureRecordMetadataAsync(tab, selected[0], forceRefresh);
            if (tab.Fields.Count == 0)
            {
                tab.Clear();
                tab.EmptyMessage = UiText.T("Compare_RecordsNoMetadataFmt", tab.ObjectApiName);
                StatusMessage = tab.EmptyMessage;
                return;
            }

            var request = tab.BuildRequest();
            if (request.Fields.Count == 0)
            {
                tab.Clear();
                tab.EmptyMessage = UiText.T("Compare_RecordsNoFields");
                StatusMessage = UiText.T("Compare_RecordsNoFields");
                return;
            }

            var progress = new Progress<string>(message => StatusMessage = message);
            var results = await _recordCompare.QueryAllAsync(selected, request, progress);
            var columns = selected
                .Select(o => new OrgCompareOrgColumn(OrgInfoCacheStore.GetOrgKey(o), o.DisplayName, o.Username, o.InstanceUrl))
                .ToList();
            var table = OrgRecordCompareService.BuildTable(tab.Category, columns, results, request);
            tab.ApplyTable(table, request, DiffOnly);
            tab.IsLoaded = true;
            StatusMessage = UiText.T("Compare_SummaryFmt", table.DiffCount, table.Rows.Count);
        }
        catch (Exception ex)
        {
            _log.Error("レコード比較: 比較表の構築に失敗しました", ex);
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
        }
        finally
        {
            tab.IsLoading = false;
            IsBusy = false;
        }
    }

    /// <summary>対象オブジェクトの項目メタデータ（fields:&lt;Object&gt; セクション）を確保し、タブの候補へ反映する。</summary>
    private async Task EnsureRecordMetadataAsync(CompareRecordsCategoryViewModel tab, OrgInfo org, bool forceRefresh)
    {
        if (tab.HasMetadata && !forceRefresh)
        {
            return;
        }

        var section = await _compare.EnsureSectionAsync(org, OrgInfoSections.Fields(tab.ObjectApiName), forceRefresh);
        if (section is null)
        {
            return;
        }

        var candidates = new List<CompareFieldCandidate>();
        foreach (var row in section.Rows)
        {
            var apiName = row.Get("apiName");
            if (!string.IsNullOrWhiteSpace(apiName))
            {
                candidates.Add(new CompareFieldCandidate(apiName!, row.Get("label") ?? string.Empty));
            }
        }

        tab.SetMetadata(candidates);
    }

    /// <summary>オブジェクト候補（objects セクション）を確保して「オブジェクト項目」「レコード比較」タブへ渡す。</summary>
    private async Task EnsureObjectCandidatesAsync()
    {
        if ((_fieldsTab is null && _recordsTab is null) || _objectCandidates.Count > 0 || _candidatesLoading)
        {
            return;
        }

        _candidatesLoading = true;
        try
        {
            var org = Orgs.FirstOrDefault(o => o.IsSelected)?.Org ?? _allOrgs.FirstOrDefault();
            var section = org is null ? null : await _compare.EnsureSectionAsync(org, OrgInfoSections.Objects);
            if (section is null)
            {
                StatusMessage = UiText.T("Compare_FieldsNoObjects");
                return;
            }

            _objectCandidates.Clear();
            foreach (var row in section.Rows)
            {
                var apiName = row.Get("apiName");
                if (!string.IsNullOrWhiteSpace(apiName))
                {
                    _objectCandidates.Add(new CompareObjectCandidate(apiName!, row.Get("label") ?? string.Empty));
                }
            }

            _objectCandidates.Sort((a, b) => string.Compare(a.ApiName, b.ApiName, StringComparison.OrdinalIgnoreCase));
            _fieldsTab?.SetCandidates(_objectCandidates);
            _recordsTab?.SetCandidates(_objectCandidates);
        }
        catch (Exception ex)
        {
            _log.Error("組織比較: オブジェクト候補の取得に失敗しました", ex);
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
        }
        finally
        {
            _candidatesLoading = false;
        }
    }

    /// <summary>表示中の比較表（フィルタ適用後）を CSV へ出力する。</summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var category = SelectedCategory;
        if (category?.Table is not { Rows.Count: > 0 } table)
        {
            return;
        }

        var fileName = await _filePicker.SaveFileAsync(
            UiText.T("Compare_CsvTitle"),
            $"org-compare-{category.Id.Replace(':', '-')}-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            UiText.T("Compare_CsvFilter"));
        if (fileName is null)
        {
            return;
        }

        try
        {
            var dataTable = OrgCompareService.BuildDataTable(table, DiffOnly);
            File.WriteAllText(fileName, CsvExporter.ToCsv(dataTable), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusMessage = UiText.T("Compare_CsvSavedFmt", fileName);
        }
        catch (Exception ex)
        {
            _log.Error("組織比較: CSV 出力に失敗しました", ex);
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UiText.LanguageChanged -= OnLanguageChanged;
        if (_fieldsTab is not null)
        {
            _fieldsTab.ObjectSelectionChanged -= OnFieldsObjectChanged;
        }

        if (_recordsTab is not null)
        {
            _recordsTab.ObjectSelectionChanged -= OnRecordsObjectChanged;
            _recordsTab.RunRequested -= OnRecordsRunRequested;
            _recordsTab.DetailRequested -= OnRecordDetailRequested;
        }

        foreach (var item in Orgs)
        {
            item.PropertyChanged -= OnOrgItemPropertyChanged;
        }
    }

    private void OnOrgItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suspendRefresh || _disposed ||
            e.PropertyName != nameof(CompareOrgItemViewModel.IsSelected) ||
            sender is not CompareOrgItemViewModel item)
        {
            return;
        }

        if (Orgs.Count(o => o.IsSelected) > OrgCompareStateStore.MaxOrgs)
        {
            _suspendRefresh = true;
            item.IsSelected = false;
            _suspendRefresh = false;
            StatusMessage = UiText.T("Compare_MaxOrgsFmt", OrgCompareStateStore.MaxOrgs);
            return;
        }

        PersistState();
        InvalidateCategories();
        _ = RefreshSelectedCategoryAsync();
    }

    private Task RefreshSelectedCategoryAsync()
    {
        var category = SelectedCategory;
        return category is null ? Task.CompletedTask : LoadCategoryAsync(category, forceRefresh: false, fetchMissing: true);
    }

    private void InvalidateCategories()
    {
        foreach (var category in Categories)
        {
            category.IsLoaded = false;
        }
    }

    private async Task LoadCategoryAsync(CompareCategoryViewModel category, bool forceRefresh, bool fetchMissing)
    {
        if (category is CompareFieldsCategoryViewModel fieldsTab && string.IsNullOrEmpty(fieldsTab.ObjectApiName))
        {
            category.Clear();
            category.EmptyMessage = UiText.T("Compare_FieldsSelectObject");
            StatusMessage = UiText.T("Compare_FieldsSelectObject");
            return;
        }

        if (category is CompareRecordsCategoryViewModel recordsTab)
        {
            if (!fetchMissing && recordsTab.IsLoaded)
            {
                // 言語切替などでは再クエリしない（セルは組織データのみで UI テキストを含まない）
                return;
            }

            await LoadRecordsTabAsync(recordsTab, forceRefresh);
            return;
        }

        var selected = Orgs.Where(o => o.IsSelected).Select(o => o.Org).ToList();
        if (selected.Count < 2)
        {
            category.Clear();
            StatusMessage = UiText.T("Compare_NeedTwoOrgs");
            return;
        }

        IsBusy = true;
        category.IsLoading = true;
        StatusMessage = UiText.T("Compare_Loading");
        try
        {
            var progress = new Progress<string>(message => StatusMessage = message);
            var table = await _compare.BuildAsync(selected, category.Category, forceRefresh, fetchMissing, progress);
            category.Apply(table, DiffOnly);
            category.IsLoaded = true;
            StatusMessage = UiText.T("Compare_SummaryFmt", table.DiffCount, table.Rows.Count);
        }
        catch (Exception ex)
        {
            _log.Error("組織比較: 比較表の構築に失敗しました", ex);
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
        }
        finally
        {
            category.IsLoading = false;
            IsBusy = false;
        }
    }

    private void OnLanguageChanged()
    {
        Title = UiText.T("Compare_Title");
        foreach (var category in Categories)
        {
            if (category.IsLoaded && !ReferenceEquals(category, SelectedCategory))
            {
                category.IsLoaded = false; // 選択時に再構築する
            }

            category.Relocalize(DiffOnly);
        }

        // 表示中のカテゴリだけ再構築する（API は呼ばない）
        var current = SelectedCategory;
        if (current is { IsLoaded: true })
        {
            _ = LoadCategoryAsync(current, forceRefresh: false, fetchMissing: false);
        }
    }

    private void PersistState()
    {
        if (_suspendRefresh || _disposed)
        {
            return;
        }

        try
        {
            _state.Save(new OrgCompareState
            {
                OrgUsernames = Orgs.Where(o => o.IsSelected).Select(o => o.Org.Username).ToList(),
                CategoryId = SelectedCategory?.Id,
                DiffOnly = DiffOnly,
                FieldsObject = _fieldsTab?.ObjectApiName,
                RecordObject = _recordsTab?.ObjectApiName,
                RecordKeyField = _recordsTab?.SelectedKeyField?.ApiName,
                RecordFields = _recordsTab?.Fields.Where(f => f.IsSelected).Select(f => f.ApiName).ToList() ?? new List<string>(),
                RecordLimit = _recordsTab?.Limit ?? 0,
            });
        }
        catch (Exception ex)
        {
            _log.Warn($"組織比較: 状態の保存に失敗しました: {ex.Message}");
        }
    }
}
