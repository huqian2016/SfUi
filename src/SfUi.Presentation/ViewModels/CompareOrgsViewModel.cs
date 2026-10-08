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
    private readonly OrgCompareStateStore _state;
    private readonly IFilePickerService _filePicker;
    private readonly ToolLauncherService _toolLauncher;
    private readonly AppLog _log;
    private readonly List<OrgInfo> _allOrgs = new();
    private bool _suspendRefresh;
    private bool _disposed;

    public CompareOrgsViewModel(OrgCompareService compare, OrgCompareStateStore state, IFilePickerService filePicker, ToolLauncherService toolLauncher, AppLog log)
    {
        _compare = compare;
        _state = state;
        _filePicker = filePicker;
        _toolLauncher = toolLauncher;
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
        return category is null ? Task.CompletedTask : LoadCategoryAsync(category, forceRefresh: false, fetchMissing: true);
    }

    partial void OnSelectedCategoryChanged(CompareCategoryViewModel? value)
    {
        if (_suspendRefresh || _disposed || value is null || value.IsLoaded || value.IsLoading)
        {
            return;
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
            });
        }
        catch (Exception ex)
        {
            _log.Warn($"組織比較: 状態の保存に失敗しました: {ex.Message}");
        }
    }
}
