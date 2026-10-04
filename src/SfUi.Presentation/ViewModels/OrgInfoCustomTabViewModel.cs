using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>カスタムタブ 1 行分の表示値。</summary>
public sealed record CustomItemView(string Label, string Value, string SourceName, string FetchedAtText, string? Link)
{
    public bool HasLink => !string.IsNullOrEmpty(Link);
}

/// <summary>
/// マイ設定のカスタムタブ 1 つ分の ViewModel。
/// カタログ項目の値をキャッシュから集約して表示し、再取得では参照元セクションのみ取得する。
/// </summary>
public partial class OrgInfoCustomTabViewModel : ObservableObject, IOrgInfoTab, IDisposable
{
    private readonly OrgInfo _org;
    private readonly string _orgKey;
    private readonly OrgInfoService _service;
    private readonly OrgInfoCacheStore _cache;
    private readonly AppLog _log;
    private readonly ToolLauncherService _toolLauncher;

    public OrgInfoCustomTabViewModel(
        string id,
        string name,
        IEnumerable<string> itemIds,
        OrgInfo org,
        string orgKey,
        OrgInfoService service,
        OrgInfoCacheStore cache,
        AppLog log,
        ToolLauncherService toolLauncher)
    {
        Id = id;
        _name = name;
        _org = org;
        _orgKey = orgKey;
        _service = service;
        _cache = cache;
        _log = log;
        _toolLauncher = toolLauncher;
        ItemIds = FilterItems(itemIds);
        _cache.SectionUpdated += OnSectionUpdated;
        Reload();
    }

    /// <summary>カスタムタブの識別子（preferences.json に保存）。</summary>
    public string Id { get; }

    /// <summary>カタログ項目 ID の並び（定義）。</summary>
    public IReadOnlyList<string> ItemIds { get; private set; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private string _fetchedAtText = UiText.T("OrgInfo_NotFetched");

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private string _emptyMessage = string.Empty;

    /// <summary>項目編集の要求（マイ設定タブへ移動する。ウィンドウ VM が処理）。</summary>
    public event Action<OrgInfoCustomTabViewModel>? EditRequested;

    public ObservableCollection<CustomItemView> Items { get; } = new();

    public string Title => Name;

    public bool HasData => ItemIds.Count > 0;

    public DateTimeOffset? FetchedAt
    {
        get
        {
            var cache = _cache.Load(_orgKey);
            DateTimeOffset? max = null;
            foreach (var sectionId in SourceSectionIds())
            {
                if (cache.Sections.TryGetValue(sectionId, out var section)
                    && section.FetchedAt is { } fetchedAt
                    && (max is null || fetchedAt > max))
                {
                    max = fetchedAt;
                }
            }

            return max;
        }
    }

    public bool CanRefresh => !IsLoading;

    /// <summary>参照元セクションの再取得 → キャッシュ更新（SectionUpdated 経由で表示へ反映）。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorText = null;
        try
        {
            foreach (var sectionId in SourceSectionIds())
            {
                var section = await _service.FetchSectionAsync(_org, sectionId);
                _cache.UpsertSection(_orgKey, section);
            }

            Reload();
        }
        catch (SalesforceApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden
            || string.Equals(ex.ErrorCode, "INSUFFICIENT_ACCESS", StringComparison.OrdinalIgnoreCase))
        {
            ErrorText = UiText.T("OrgInfo_PermissionDeniedFmt", ex.Message);
            _log.Error($"マイ設定の取得権限が不足: {_orgKey}/{Id}", ex);
        }
        catch (Exception ex)
        {
            ErrorText = UiText.T("OrgInfo_LoadFailedFmt", ex.Message);
            _log.Error($"マイ設定の再取得に失敗: {_orgKey}/{Id}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Edit() => EditRequested?.Invoke(this);

    [RelayCommand]
    private void OpenLink(CustomItemView? row)
    {
        if (row is not { Link: { Length: > 0 } url })
        {
            return;
        }

        var result = _toolLauncher.LaunchBrowser(url);
        if (!result.Success)
        {
            ErrorText = result.Message;
        }
    }

    /// <summary>定義（名前・項目）を更新して再描画する（マイ設定の保存）。</summary>
    public void Update(string name, IReadOnlyList<string> itemIds)
    {
        Name = name;
        ItemIds = FilterItems(itemIds);
        Reload();
    }

    /// <summary>キャッシュから値を読み直して表示を作り直す。</summary>
    public void Reload()
    {
        var cache = _cache.Load(_orgKey);
        OrgInfoSection? GetSection(string sectionId) => cache.Sections.TryGetValue(sectionId, out var section) ? section : null;

        Items.Clear();
        foreach (var itemId in ItemIds)
        {
            var labelKey = OrgInfoCatalog.LabelKeyFor(itemId);
            var label = labelKey is null ? itemId : UiText.T(labelKey);
            var value = OrgInfoCatalog.Resolve(itemId, GetSection);
            var text = OrgInfoDisplay.FormatValueToken(value?.Text) ?? string.Empty;
            var sourceName = value is { IsStat: true }
                ? UiText.T("OrgInfo_Catalog_Stats")
                : value?.SourceSectionId is { } sourceId
                    ? UiText.T(OrgInfoSections.Find(sourceId)?.TitleKey ?? sourceId)
                    : string.Empty;
            var fetchedAt = value?.SourceSectionId is { } sectionId
                            && cache.Sections.TryGetValue(sectionId, out var sourceSection)
                            && sourceSection.FetchedAt is { } timestamp
                ? UiText.T("OrgInfo_FetchedAtFmt", OrgInfoSectionViewModel.FormatTimestamp(timestamp))
                : UiText.T("OrgInfo_NotFetched");

            Items.Add(new CustomItemView(label, text, sourceName, fetchedAt, value?.Link));
        }

        UpdateDerived();
    }

    public void Relocalize() => Reload();

    /// <summary>AI パネルへ添付するテキスト（項目 / 値 / 取得元 / 取得時刻）。</summary>
    public string? BuildAttachmentText(int maxChars)
    {
        if (ItemIds.Count == 0)
        {
            return null;
        }

        var headers = new[]
        {
            UiText.T("OrgInfo_Col_Item"),
            UiText.T("OrgInfo_Col_Value"),
            UiText.T("OrgInfo_Col_Source"),
            UiText.T("OrgInfo_Col_FetchedAt"),
        };
        var rows = Items
            .Select(row => (IReadOnlyList<string>)new[] { row.Label, row.Value, row.SourceName, row.FetchedAtText })
            .ToList();

        var title = UiText.T("OrgInfo_Ai_TabHeaderFmt", Title, Items.Count, FetchedAtText);
        return OrgInfoAttachment.Build(title, headers, rows, maxChars);
    }

    /// <summary>タブの UI オートメーション名などに使われる表示名。</summary>
    public override string ToString() => Title;

    public void Dispose()
    {
        _cache.SectionUpdated -= OnSectionUpdated;
        Items.Clear();
    }

    /// <summary>参照元セクション（再取得・他ウィンドウ更新の反映に使う）。</summary>
    public IReadOnlyList<string> SourceSectionIds() =>
        ItemIds.SelectMany(OrgInfoCatalog.SourceSectionIds).Distinct(StringComparer.Ordinal).ToList();

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Title));

    private void OnSectionUpdated(string orgKey, string sectionId)
    {
        if (!string.Equals(orgKey, _orgKey, StringComparison.OrdinalIgnoreCase) || IsLoading)
        {
            return;
        }

        if (SourceSectionIds().Contains(sectionId, StringComparer.Ordinal))
        {
            Reload();
        }
    }

    private void UpdateDerived()
    {
        CountText = UiText.T("OrgInfo_Custom_CountFmt", Items.Count);
        EmptyMessage = ItemIds.Count == 0
            ? UiText.T("OrgInfo_Custom_EmptyItems")
            : Items.All(i => string.IsNullOrEmpty(i.Value))
                ? UiText.T("OrgInfo_Custom_NoData")
                : string.Empty;

        FetchedAtText = FetchedAt is { } fetchedAt
            ? UiText.T("OrgInfo_FetchedAtFmt", OrgInfoSectionViewModel.FormatTimestamp(fetchedAt))
            : UiText.T("OrgInfo_NotFetched");
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(FetchedAt));
    }

    private static List<string> FilterItems(IEnumerable<string> itemIds) =>
        itemIds.Where(id => OrgInfoCatalog.Find(id) is not null).Distinct(StringComparer.Ordinal).ToList();
}
