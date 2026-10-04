using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// オブジェクトアクセス タブ。選択中オブジェクトに対する
/// Permission Sets / Permission Set Groups / Profiles の権限を一覧化する。
/// </summary>
public sealed partial class ObjectAccessViewModel : ObservableObject, IDisposable
{
    private readonly PermissionAccessService _service;
    private readonly AppLog _log;
    private DataIoViewModel? _owner;
    private bool _activated;
    private bool _stale = true;
    private bool _reloadQueued;

    public ObjectAccessViewModel(PermissionAccessService service, AppLog log)
    {
        _service = service;
        _log = log;
    }

    public ObservableCollection<ObjectAccessRowViewModel> Rows { get; } = new();

    /// <summary>全行（検索フィルタ前）。</summary>
    private readonly List<ObjectAccessRowViewModel> _allRows = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public void Attach(DataIoViewModel owner)
    {
        _owner = owner;
        owner.DescribeChanged += OnDescribeChanged;
    }

    /// <summary>タブ初回表示時に呼ぶ（表示済みなら対象オブジェクト変更時のみ再読込）。</summary>
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
            _allRows.Clear();
            Rows.Clear();
            SummaryText = string.Empty;
            StatusMessage = UiText.T("Access_SelectObjectFirst");
            _stale = false;
            return;
        }

        IsBusy = true;
        StatusMessage = UiText.T("DataIo_Loading");
        try
        {
            var rows = await _service.GetObjectAccessAsync(_owner.TargetOrg, describe.Name);
            _allRows.Clear();
            foreach (var row in rows)
            {
                _allRows.Add(new ObjectAccessRowViewModel(row));
            }

            ApplyFilter();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("オブジェクトアクセスの取得に失敗しました", ex);
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
    }

    /// <summary>検索テキスト（スペース区切りは AND）で行を絞り込む。</summary>
    private void ApplyFilter()
    {
        var terms = ParseTerms(SearchText);
        Rows.Clear();
        foreach (var row in _allRows)
        {
            if (terms.Count > 0 && !terms.All(t => row.SearchText.Contains(t, StringComparison.Ordinal)))
            {
                continue;
            }

            Rows.Add(row);
        }

        SummaryText = _allRows.Count == 0
            ? string.Empty
            : terms.Count == 0
                ? UiText.T("Access_RowCountFmt", _allRows.Count)
                : UiText.T("Access_FilteredFmt", Rows.Count, _allRows.Count);
    }

    private static IReadOnlyList<string> ParseTerms(string? filter) =>
        string.IsNullOrWhiteSpace(filter)
            ? Array.Empty<string>()
            : filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant())
                .ToArray();

    public void Dispose()
    {
        if (_owner is not null)
        {
            _owner.DescribeChanged -= OnDescribeChanged;
            _owner = null;
        }
    }
}
