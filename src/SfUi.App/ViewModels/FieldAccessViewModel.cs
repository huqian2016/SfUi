using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>項目アクセス マトリクスの 1 行（項目 + 各主体のセル）。</summary>
public sealed class FieldMatrixRowViewModel
{
    public FieldMatrixRowViewModel(string label, string[] cells)
    {
        Label = label;
        Cells = cells;
        SearchText = label.ToLowerInvariant();
    }

    public string Label { get; }

    /// <summary>主体（列）順のセル文字列（R / E / R, E / 空欄）。</summary>
    public string[] Cells { get; }

    /// <summary>検索用（ラベル (API 名) の小文字）。</summary>
    public string SearchText { get; }
}

/// <summary>
/// 項目アクセス タブ。選択中オブジェクトの項目（行）× 各権限主体（列）のマトリクスを表示する。
/// セルは FieldPermissions の明示行のみ（R = 読取 / E = 編集）。
/// </summary>
public sealed partial class FieldAccessViewModel : ObservableObject, IDisposable
{
    private readonly PermissionAccessService _service;
    private readonly AppLog _log;
    private DataIoViewModel? _owner;
    private PermissionCatalog? _catalog;
    private FieldAccessSnapshot? _snapshot;
    private bool _activated;
    private bool _stale = true;
    private bool _reloadQueued;

    public FieldAccessViewModel(PermissionAccessService service, AppLog log)
    {
        _service = service;
        _log = log;
    }

    /// <summary>列・行の再構築が必要になったとき（View が列を組み直す）。</summary>
    public event Action? MatrixChanged;

    public ObservableCollection<FieldMatrixRowViewModel> Rows { get; } = new();

    /// <summary>現在表示中の列（主体）。</summary>
    public IReadOnlyList<PermissionSubject> HeaderSubjects { get; private set; } = Array.Empty<PermissionSubject>();

    [ObservableProperty]
    private bool _showProfiles = true;

    [ObservableProperty]
    private bool _showPermissionSets = true;

    [ObservableProperty]
    private bool _showGroups = true;

    [ObservableProperty]
    private string _columnFilter = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public void Attach(DataIoViewModel owner)
    {
        _owner = owner;
        owner.DescribeChanged += OnDescribeChanged;
    }

    /// <summary>タブ初回表示時に呼ぶ。</summary>
    public async Task EnsureLoadedAsync()
    {
        _activated = true;
        if (!_stale)
        {
            return;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_owner is not null)
        {
            _service.Invalidate(_owner.TargetOrg);
        }

        _stale = true;
        await RefreshAsync();
    }

    partial void OnShowProfilesChanged(bool value) => Rebuild();

    partial void OnShowPermissionSetsChanged(bool value) => Rebuild();

    partial void OnShowGroupsChanged(bool value) => Rebuild();

    partial void OnColumnFilterChanged(string value) => Rebuild();

    partial void OnSearchTextChanged(string value) => Rebuild();

    private void OnDescribeChanged()
    {
        _stale = true;
        if (_activated)
        {
            _ = RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (_owner is null)
        {
            return;
        }

        if (IsBusy)
        {
            _reloadQueued = true;
            return;
        }

        var describe = _owner.Describe;
        if (string.IsNullOrEmpty(_owner.TargetOrg) || describe is null)
        {
            _catalog = null;
            _snapshot = null;
            Rows.Clear();
            HeaderSubjects = Array.Empty<PermissionSubject>();
            SummaryText = string.Empty;
            StatusMessage = UiText.T("Access_SelectObjectFirst");
            _stale = false;
            MatrixChanged?.Invoke();
            return;
        }

        IsBusy = true;
        StatusMessage = UiText.T("DataIo_Loading");
        try
        {
            _catalog = await _service.GetCatalogAsync(_owner.TargetOrg);
            _snapshot = await _service.GetFieldAccessAsync(_owner.TargetOrg, describe.Name);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _catalog = null;
            _snapshot = null;
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("項目アクセスの取得に失敗しました", ex);
        }
        finally
        {
            IsBusy = false;
            _stale = false;
            if (_reloadQueued)
            {
                _reloadQueued = false;
                _stale = true;
                _ = RefreshAsync();
            }
        }

        Rebuild();
    }

    /// <summary>フィルタ適用後の列と行を再構築する（クエリなし・スナップショットから）。</summary>
    private void Rebuild()
    {
        var describe = _owner?.Describe;
        var subjects = _catalog?.Subjects ?? (IReadOnlyList<PermissionSubject>)Array.Empty<PermissionSubject>();
        var filter = ColumnFilter?.Trim() ?? string.Empty;

        var filtered = subjects
            .Where(s => s.Kind switch
            {
                PermissionSubjectKind.Profile => ShowProfiles,
                PermissionSubjectKind.PermissionSet => ShowPermissionSets,
                _ => ShowGroups,
            })
            .Where(s => filter.Length == 0
                || s.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || s.ApiName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        HeaderSubjects = filtered;

        Rows.Clear();
        if (describe is not null && _snapshot is not null && filtered.Count > 0)
        {
            var terms = ParseTerms(SearchText);
            foreach (var field in describe.Fields)
            {
                var label = $"{field.Label} ({field.Name})";
                if (terms.Count > 0 && !terms.All(t => label.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var cells = new string[filtered.Count];
                var fullName = $"{describe.Name}.{field.Name}";
                for (var i = 0; i < filtered.Count; i++)
                {
                    cells[i] = _snapshot.BySubject.TryGetValue(filtered[i].Id, out var map) && map.TryGetValue(fullName, out var cell)
                        ? FormatCell(cell)
                        : string.Empty;
                }

                Rows.Add(new FieldMatrixRowViewModel(label, cells));
            }

            SummaryText = UiText.T("FieldAccess_SummaryFmt", Rows.Count, filtered.Count);
        }
        else
        {
            SummaryText = string.Empty;
        }

        MatrixChanged?.Invoke();
    }

    private static IReadOnlyList<string> ParseTerms(string? filter) =>
        string.IsNullOrWhiteSpace(filter)
            ? Array.Empty<string>()
            : filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant())
                .ToArray();

    /// <summary>セル文字列（R / E / R, E）。</summary>
    public static string FormatCell(FieldAccessCell cell) => cell switch
    {
        { Read: true, Edit: true } => "R, E",
        { Read: true } => "R",
        { Edit: true } => "E",
        _ => string.Empty,
    };

    public void Dispose()
    {
        if (_owner is not null)
        {
            _owner.DescribeChanged -= OnDescribeChanged;
            _owner = null;
        }
    }
}
