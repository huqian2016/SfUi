using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// レコードアクセス タブ。任意 SOQL で抽出したレコードについて、
/// 選択ユーザーごとのレコードアクセス権（UserRecordAccess）をページング表示する。
/// ページサイズ 200 は UserRecordAccess の 1 クエリ上限（200 行）に合わせている。
/// </summary>
public sealed partial class RecordAccessViewModel : ObservableObject, IDisposable
{
    public const int PageSize = 200;

    private readonly RecordAccessService _service;
    private readonly AppLog _log;
    private DataIoViewModel? _owner;

    private readonly List<RecordAccessUserViewModel> _allUsers = new();
    private readonly List<IReadOnlyDictionary<string, string?>> _allRecords = new();
    private readonly List<IReadOnlyDictionary<string, string?>> _filtered = new();

    /// <summary>userId → recordId → フラグ（取得済みキャッシュ）。</summary>
    private readonly Dictionary<string, Dictionary<string, UserRecordAccessFlags>> _cache = new(StringComparer.Ordinal);

    private int _pageIndex;
    private int _pageCount;
    private int _pageSize = PageSize;
    private string _lastAutoSoql = string.Empty;
    private bool _activated;
    private bool _usersLoaded;
    private bool _recordsLoaded;
    private bool _selectionBatch;
    private bool _accessReloadQueued;

    public RecordAccessViewModel(RecordAccessService service, AppLog log)
    {
        _service = service;
        _log = log;
    }

    /// <summary>ユーザー列など、列の再構築が必要になったとき（View が列を組み直す）。</summary>
    public event Action? ColumnsChanged;

    public ObservableCollection<RecordAccessUserViewModel> VisibleUsers { get; } = new();

    public ObservableCollection<RecordAccessRowViewModel> Rows { get; } = new();

    public ObservableCollection<string> DisplayFields { get; } = new();

    public IReadOnlyList<RecordAccessUserViewModel> SelectedUsers => _allUsers.Where(u => u.IsSelected).ToList();

    [ObservableProperty]
    private string _soqlText = string.Empty;

    [ObservableProperty]
    private string _userFilter = string.Empty;

    [ObservableProperty]
    private string _recordFilter = string.Empty;

    [ObservableProperty]
    private string? _selectedDisplayField;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _pageText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public void Attach(DataIoViewModel owner)
    {
        _owner = owner;
        owner.DescribeChanged += OnDescribeChanged;
    }

    /// <summary>タブ初回表示時に呼ぶ（既定 SOQL の設定と有効ユーザー一覧の取得）。</summary>
    public async Task EnsureLoadedAsync()
    {
        _activated = true;
        UpdateAutoSoql();
        await EnsureUsersAsync();
    }

    private void OnDescribeChanged()
    {
        if (_activated)
        {
            UpdateAutoSoql();
        }
    }

    /// <summary>既定の対象レコード SOQL（Id + Name 等）を、ユーザー未編集のときだけ更新する。</summary>
    private void UpdateAutoSoql()
    {
        var describe = _owner?.Describe;
        var text = describe is null ? string.Empty : BuildDefaultSoql(describe);
        if (string.IsNullOrWhiteSpace(SoqlText) || string.Equals(SoqlText, _lastAutoSoql, StringComparison.Ordinal))
        {
            SoqlText = text;
            _lastAutoSoql = text;
        }
    }

    /// <summary>選択中オブジェクトから既定 SOQL を組み立てる（Id + Name、Name が無ければ先頭項目）。</summary>
    public static string BuildDefaultSoql(DataIoObjectDescribe describe)
    {
        var fields = new List<string> { "Id" };
        if (describe.Fields.Any(f => string.Equals(f.Name, "Name", StringComparison.OrdinalIgnoreCase)))
        {
            fields.Add("Name");
        }
        else
        {
            var first = describe.Fields.FirstOrDefault(f => !string.Equals(f.Name, "Id", StringComparison.OrdinalIgnoreCase));
            if (first is not null)
            {
                fields.Add(first.Name);
            }
        }

        return $"SELECT {string.Join(", ", fields)} FROM {describe.Name}";
    }

    private async Task EnsureUsersAsync()
    {
        if (_usersLoaded || _owner is null || string.IsNullOrEmpty(_owner.TargetOrg))
        {
            return;
        }

        _usersLoaded = true;
        try
        {
            var users = await _service.ListActiveUsersAsync(_owner.TargetOrg);
            _allUsers.Clear();
            foreach (var user in users)
            {
                _allUsers.Add(new RecordAccessUserViewModel(user, OnUserSelectionChanged));
            }

            ApplyUserFilter();
        }
        catch (Exception ex)
        {
            _usersLoaded = false;
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("有効ユーザーの取得に失敗しました", ex);
        }
    }

    [RelayCommand]
    private void SelectAllUsers()
    {
        _selectionBatch = true;
        foreach (var user in VisibleUsers)
        {
            user.IsSelected = true;
        }

        _selectionBatch = false;
        OnUserSelectionChanged();
    }

    [RelayCommand]
    private void ClearUsers()
    {
        _selectionBatch = true;
        foreach (var user in _allUsers)
        {
            user.IsSelected = false;
        }

        _selectionBatch = false;
        OnUserSelectionChanged();
    }

    private void OnUserSelectionChanged(RecordAccessUserViewModel user)
    {
        if (_selectionBatch)
        {
            return;
        }

        OnUserSelectionChanged();
    }

    private void OnUserSelectionChanged()
    {
        UpdateSummary();
        if (_recordsLoaded)
        {
            RefreshRows();
            ColumnsChanged?.Invoke();
            _ = LoadPageAccessAsync();
        }
    }

    partial void OnUserFilterChanged(string value) => ApplyUserFilter();

    partial void OnRecordFilterChanged(string value)
    {
        if (_recordsLoaded)
        {
            ApplyRecordFilter(resetPage: true);
            _ = LoadPageAccessAsync();
        }
    }

    partial void OnSelectedDisplayFieldChanged(string? value)
    {
        if (_recordsLoaded)
        {
            RefreshRows();
            ColumnsChanged?.Invoke();
        }
    }

    private void ApplyUserFilter()
    {
        var filter = UserFilter?.Trim() ?? string.Empty;
        VisibleUsers.Clear();
        foreach (var user in _allUsers)
        {
            if (filter.Length == 0
                || user.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || user.Username.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                VisibleUsers.Add(user);
            }
        }

        UpdateSummary();
    }

    [RelayCommand]
    private void MoveFirst() => MoveTo(0);

    [RelayCommand]
    private void MovePrev() => MoveTo(_pageIndex - 1);

    [RelayCommand]
    private void MoveNext() => MoveTo(_pageIndex + 1);

    [RelayCommand]
    private void MoveLast() => MoveTo(_pageCount - 1);

    private void MoveTo(int index)
    {
        if (_filtered.Count == 0)
        {
            return;
        }

        var clamped = Math.Clamp(index, 0, _pageCount - 1);
        if (clamped == _pageIndex)
        {
            return;
        }

        _pageIndex = clamped;
        RefreshRows();
        UpdatePageText();
        _ = LoadPageAccessAsync();
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (_owner is null || IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SoqlText))
        {
            StatusMessage = UiText.T("RecordAccess_NeedSoql");
            return;
        }

        await EnsureUsersAsync();

        IsBusy = true;
        StatusMessage = UiText.T("RecordAccess_Running");
        try
        {
            var soql = SoqlText.Trim();
            _lastAutoSoql = soql;
            var result = await _service.QueryRecordsAsync(_owner.TargetOrg, soql);
            _allRecords.Clear();
            _allRecords.AddRange(result.Records);
            _cache.Clear();
            _recordsLoaded = true;
            UpdateDisplayFields(result.Columns);
            ApplyRecordFilter(resetPage: true);
            SummaryText = result.Truncated
                ? UiText.T("RecordAccess_TruncatedFmt", RecordAccessService.DefaultMaxRecords)
                : UiText.T("Access_RowCountFmt", _allRecords.Count);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("対象レコードの取得に失敗しました", ex);
        }
        finally
        {
            IsBusy = false;
        }

        if (_recordsLoaded)
        {
            await LoadPageAccessAsync();
        }
    }

    private void UpdateDisplayFields(IReadOnlyList<string> columns)
    {
        DisplayFields.Clear();
        foreach (var column in columns.Where(c => !string.Equals(c, "Id", StringComparison.OrdinalIgnoreCase)))
        {
            DisplayFields.Add(column);
        }

        SelectedDisplayField = DisplayFields.FirstOrDefault(f => string.Equals(f, "Name", StringComparison.OrdinalIgnoreCase))
            ?? DisplayFields.FirstOrDefault();
    }

    private void ApplyRecordFilter(bool resetPage)
    {
        var filter = RecordFilter?.Trim() ?? string.Empty;
        _filtered.Clear();
        foreach (var record in _allRecords)
        {
            if (filter.Length == 0 || Matches(record, filter))
            {
                _filtered.Add(record);
            }
        }

        if (resetPage)
        {
            _pageIndex = 0;
        }

        _pageCount = Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)_pageSize));
        if (_pageIndex >= _pageCount)
        {
            _pageIndex = _pageCount - 1;
        }

        RefreshRows();
        UpdatePageText();
        ColumnsChanged?.Invoke();
    }

    private bool Matches(IReadOnlyDictionary<string, string?> record, string filter)
    {
        if (record.TryGetValue("Id", out var id) && id is not null && id.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SelectedDisplayField is { } field
            && record.TryGetValue(field, out var value)
            && value is not null
            && value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<IReadOnlyDictionary<string, string?>> CurrentPageRecords() =>
        _filtered.Skip(_pageIndex * _pageSize).Take(_pageSize).ToList();

    private void RefreshRows()
    {
        var users = SelectedUsers;
        Rows.Clear();
        foreach (var record in CurrentPageRecords())
        {
            var id = record.TryGetValue("Id", out var idValue) ? idValue ?? string.Empty : string.Empty;
            var display = SelectedDisplayField is { } field && record.TryGetValue(field, out var displayValue)
                ? displayValue ?? string.Empty
                : string.Empty;
            var cells = new string[users.Count * 4];
            for (var i = 0; i < users.Count; i++)
            {
                if (_cache.TryGetValue(users[i].Id, out var flagsById) && flagsById.TryGetValue(id, out var flags))
                {
                    cells[(i * 4) + 0] = Tick(flags.Read);
                    cells[(i * 4) + 1] = Tick(flags.Edit);
                    cells[(i * 4) + 2] = Tick(flags.Delete);
                    cells[(i * 4) + 3] = Tick(flags.Transfer);
                }
                else
                {
                    cells[(i * 4) + 0] = string.Empty;
                    cells[(i * 4) + 1] = string.Empty;
                    cells[(i * 4) + 2] = string.Empty;
                    cells[(i * 4) + 3] = string.Empty;
                }
            }

            Rows.Add(new RecordAccessRowViewModel(id, display, BuildLink(record, id), cells));
        }
    }

    private string BuildLink(IReadOnlyDictionary<string, string?> record, string id)
    {
        var instance = _owner?.Org?.InstanceUrl;
        var type = record.TryGetValue(RecordAccessService.TypeKey, out var typeValue) ? typeValue : null;
        if (string.IsNullOrEmpty(instance) || string.IsNullOrEmpty(type) || string.IsNullOrEmpty(id))
        {
            return string.Empty;
        }

        return instance.TrimEnd('/') + $"/lightning/r/{type}/{id}/view";
    }

    /// <summary>現在ページ × 選択ユーザーのアクセス権を取得する（不足分のみ）。</summary>
    private async Task LoadPageAccessAsync()
    {
        if (_owner is null || !_recordsLoaded)
        {
            return;
        }

        var users = SelectedUsers;
        if (users.Count == 0)
        {
            StatusMessage = UiText.T("RecordAccess_NeedUsers");
            return;
        }

        if (IsBusy)
        {
            _accessReloadQueued = true;
            return;
        }

        var pageIds = CurrentPageRecords()
            .Select(r => r.TryGetValue("Id", out var idValue) ? idValue : null)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToList();
        if (pageIds.Count == 0)
        {
            return;
        }

        var missingUsers = users
            .Where(u => !_cache.TryGetValue(u.Id, out var flagsById) || pageIds.Any(id => !flagsById.ContainsKey(id)))
            .Select(u => u.Id)
            .ToList();
        if (missingUsers.Count == 0)
        {
            StatusMessage = string.Empty;
            return;
        }

        IsBusy = true;
        StatusMessage = UiText.T("RecordAccess_Loading");
        try
        {
            var map = await _service.GetAccessFlagsForUsersAsync(_owner.TargetOrg, missingUsers, pageIds);
            foreach (var pair in map)
            {
                if (!_cache.TryGetValue(pair.Key, out var flagsById))
                {
                    flagsById = new Dictionary<string, UserRecordAccessFlags>(StringComparer.Ordinal);
                    _cache[pair.Key] = flagsById;
                }

                foreach (var entry in pair.Value)
                {
                    flagsById[entry.Key] = entry.Value;
                }
            }

            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("レコードアクセスの取得に失敗しました", ex);
        }
        finally
        {
            IsBusy = false;
            if (_accessReloadQueued)
            {
                _accessReloadQueued = false;
                _ = LoadPageAccessAsync();
            }
        }

        RefreshRows();
    }

    [RelayCommand]
    private void OpenRecord(RecordAccessRowViewModel? row)
    {
        if (string.IsNullOrEmpty(row?.LinkUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(row.LinkUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error($"レコードを開けませんでした: {row.LinkUrl}", ex);
        }
    }

    private void UpdateSummary()
    {
        if (!_recordsLoaded)
        {
            SummaryText = string.Empty;
            return;
        }

        SummaryText = UiText.T("RecordAccess_SummaryFmt", _allRecords.Count, SelectedUsers.Count);
    }

    private void UpdatePageText()
    {
        PageText = _filtered.Count == 0
            ? string.Empty
            : UiText.T("RecordAccess_PageFmt", _pageIndex + 1, _pageCount, _filtered.Count);
    }

    private static string Tick(bool value) => value ? "✓" : "−";

    public void Dispose()
    {
        if (_owner is not null)
        {
            _owner.DescribeChanged -= OnDescribeChanged;
            _owner = null;
        }
    }
}
