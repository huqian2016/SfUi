using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>ピッカーの利用可能項目。</summary>
public sealed record AvailableItemView(string ItemId, string Label, string GroupName)
{
    public override string ToString() => Label;
}

/// <summary>ピッカーの選択済み項目。</summary>
public sealed record SelectedItemView(string ItemId, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// マイ設定タブ（カスタムタブの作成・名前変更・削除と、項目カタログの選択・並べ替え）。
/// 保存・作成・削除の実処理（preferences への永続化とウィンドウのタブ再構築）はウィンドウ VM が行う。
/// </summary>
public partial class OrgInfoMySettingsViewModel : ObservableObject, IOrgInfoTab, IDisposable
{
    private bool _loadingSelection;

    public OrgInfoMySettingsViewModel()
    {
        AvailableView = CollectionViewSource.GetDefaultView(AvailableItems);
        AvailableView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AvailableItemView.GroupName)));
        AvailableView.Filter = FilterAvailable;
        RebuildAvailable();
    }

    [ObservableProperty]
    private string _title = UiText.T("OrgInfo_MySettings");

    [ObservableProperty]
    private string _hintText = UiText.T("OrgInfo_Custom_Hint");

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private OrgInfoCustomTabViewModel? _selectedTab;

    [ObservableProperty]
    private string _editName = string.Empty;

    /// <summary>カスタムタブの一覧（保存済み定義）。</summary>
    public ObservableCollection<OrgInfoCustomTabViewModel> Tabs { get; } = new();

    /// <summary>利用可能な項目（グループ化 + 検索フィルター付き）。</summary>
    public ObservableCollection<AvailableItemView> AvailableItems { get; } = new();

    /// <summary>編集中の選択済み項目（並べ替え可）。</summary>
    public ObservableCollection<SelectedItemView> SelectedItems { get; } = new();

    public ICollectionView AvailableView { get; }

    public bool HasTabs => Tabs.Count > 0;

    public bool HasSelection => SelectedTab is not null;

    // ---- ウィンドウ VM が処理する要求 ----

    public event Action? CreateRequested;

    public event Action<string>? DeleteRequested;

    public event Action<string, string, IReadOnlyList<string>>? SaveRequested;

    public bool HasData => Tabs.Count > 0;

    public DateTimeOffset? FetchedAt => null;

    public string? BuildAttachmentText(int maxChars) => null;

    /// <summary>カスタムタブ一覧を差し替える（selectId があればそれを選択）。</summary>
    public void SetTabs(IEnumerable<OrgInfoCustomTabViewModel> tabs, string? selectId = null)
    {
        var previous = selectId ?? SelectedTab?.Id;
        Tabs.Clear();
        foreach (var tab in tabs)
        {
            Tabs.Add(tab);
        }

        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(HasData));
        SelectedTab = Tabs.FirstOrDefault(t => t.Id == previous) ?? Tabs.FirstOrDefault();
    }

    public void SelectTab(string tabId)
    {
        var tab = Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is not null)
        {
            SelectedTab = tab;
        }
    }

    /// <summary>保存完了の表示。</summary>
    public void NotifySaved() => StatusText = UiText.T("OrgInfo_Custom_Saved");

    [RelayCommand]
    private void Create() => CreateRequested?.Invoke();

    [RelayCommand]
    private void Delete()
    {
        if (SelectedTab is { } tab)
        {
            DeleteRequested?.Invoke(tab.Id);
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedTab is not { } tab)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(EditName) ? tab.Name : EditName.Trim();
        SaveRequested?.Invoke(tab.Id, name, SelectedItems.Select(s => s.ItemId).ToList());
    }

    [RelayCommand]
    private void AddItem(AvailableItemView? item)
    {
        if (item is null || SelectedTab is null)
        {
            return;
        }

        if (SelectedItems.Any(s => string.Equals(s.ItemId, item.ItemId, StringComparison.Ordinal)))
        {
            return;
        }

        SelectedItems.Add(CreateSelected(item.ItemId));
    }

    [RelayCommand]
    private void RemoveItem(SelectedItemView? item)
    {
        if (item is not null)
        {
            SelectedItems.Remove(item);
        }
    }

    [RelayCommand]
    private void MoveUp(SelectedItemView? item)
    {
        if (item is null)
        {
            return;
        }

        var index = SelectedItems.IndexOf(item);
        if (index > 0)
        {
            SelectedItems.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveDown(SelectedItemView? item)
    {
        if (item is null)
        {
            return;
        }

        var index = SelectedItems.IndexOf(item);
        if (index >= 0 && index < SelectedItems.Count - 1)
        {
            SelectedItems.Move(index, index + 1);
        }
    }

    /// <summary>タブの UI オートメーション名などに使われる表示名。</summary>
    public override string ToString() => Title;

    public void Relocalize()
    {
        Title = UiText.T("OrgInfo_MySettings");
        HintText = UiText.T("OrgInfo_Custom_Hint");
        RebuildAvailable();

        var ids = SelectedItems.Select(s => s.ItemId).ToList();
        SelectedItems.Clear();
        foreach (var itemId in ids)
        {
            SelectedItems.Add(CreateSelected(itemId));
        }
    }

    public void Dispose()
    {
    }

    partial void OnSelectedTabChanged(OrgInfoCustomTabViewModel? value)
    {
        _loadingSelection = true;
        try
        {
            EditName = value?.Name ?? string.Empty;
            SelectedItems.Clear();
            if (value is not null)
            {
                foreach (var itemId in value.ItemIds)
                {
                    SelectedItems.Add(CreateSelected(itemId));
                }
            }

            StatusText = string.Empty;
        }
        finally
        {
            _loadingSelection = false;
        }

        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_loadingSelection)
        {
            AvailableView.Refresh();
        }
    }

    private SelectedItemView CreateSelected(string itemId) =>
        new(itemId, UiText.T(OrgInfoCatalog.LabelKeyFor(itemId) ?? itemId));

    private bool FilterAvailable(object obj)
    {
        if (obj is not AvailableItemView item)
        {
            return false;
        }

        var terms = SearchText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.Length == 0
            || terms.All(t => item.Label.Contains(t, StringComparison.OrdinalIgnoreCase)
                              || item.GroupName.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private void RebuildAvailable()
    {
        AvailableItems.Clear();
        foreach (var item in OrgInfoCatalog.All)
        {
            AvailableItems.Add(new AvailableItemView(
                item.Id,
                UiText.T(item.LabelKey),
                UiText.T(OrgInfoCatalog.GroupTitleKey(item.Group))));
        }

        AvailableView?.Refresh();
    }
}
