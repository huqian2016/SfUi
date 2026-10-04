using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>レコード単位比較ウィンドウの 1 行。</summary>
public sealed class BackupCompareRecordRowViewModel
{
    private const int MaxFieldsShown = 5;
    private const int MaxValueLength = 40;

    public BackupCompareRecordRowViewModel(BackupRecordDiff diff)
    {
        KindText = UiText.T(diff.Kind switch
        {
            BackupDiffKind.Added => "BackupCompare_KindAdded",
            BackupDiffKind.Removed => "BackupCompare_KindRemoved",
            _ => "BackupCompare_KindChanged",
        });
        IsAdded = diff.Kind == BackupDiffKind.Added;
        IsRemoved = diff.Kind == BackupDiffKind.Removed;
        Id = diff.Id;
        Display = diff.Display;
        ChangeSummary = BuildSummary(diff);
        Blob = $"{KindText}\n{Id}\n{Display}\n{ChangeSummary}".ToLowerInvariant();
    }

    public string KindText { get; }

    public bool IsAdded { get; }

    public bool IsRemoved { get; }

    public string Id { get; }

    public string Display { get; }

    public string ChangeSummary { get; }

    public string Blob { get; }

    private static string BuildSummary(BackupRecordDiff diff)
    {
        if (diff.Kind != BackupDiffKind.Changed || diff.Fields.Count == 0)
        {
            return string.Empty;
        }

        var parts = diff.Fields.Take(MaxFieldsShown)
            .Select(field => $"{field.Field}: {Show(field.ValueA)} → {Show(field.ValueB)}");
        var text = string.Join("; ", parts);
        if (diff.Fields.Count > MaxFieldsShown)
        {
            text += " " + UiText.T("BackupCompare_MoreFieldsFmt", diff.Fields.Count - MaxFieldsShown);
        }

        return text;
    }

    private static string Show(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return UiText.T("BackupCompare_FieldEmpty");
        }

        return value.Length > MaxValueLength ? value[..MaxValueLength] + "…" : value;
    }
}

/// <summary>バックアップ比較のレコード単位差分を表示する別ウィンドウの ViewModel（検索 + 200 件/ページ）。</summary>
public sealed partial class BackupCompareRecordsViewModel : ObservableObject
{
    private const int PageSize = 200;

    private readonly BackupCompareService _compare;
    private readonly AppLog _log;
    private readonly List<BackupCompareRecordRowViewModel> _all = new();
    private List<BackupCompareRecordRowViewModel> _filtered = new();
    private string _backupIdA = string.Empty;
    private string _backupIdB = string.Empty;
    private string _objectName = string.Empty;
    private bool _loaded;
    private int _pageIndex;

    public BackupCompareRecordsViewModel(BackupCompareService compare, AppLog log)
    {
        _compare = compare;
        _log = log;
    }

    public ObservableCollection<BackupCompareRecordRowViewModel> PageRows { get; } = new();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _pageText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _truncated;

    [ObservableProperty]
    private bool _hasPrev;

    [ObservableProperty]
    private bool _hasNext;

    /// <summary>上限で打ち切られたときに表示する注意文。</summary>
    public string TruncatedText => UiText.T("BackupRecords_Truncated", BackupCompareService.MaxDetailRows);

    public void Initialize(string backupIdA, string backupIdB, string objectName, string displayName)
    {
        if (_loaded)
        {
            return;
        }

        _backupIdA = backupIdA;
        _backupIdB = backupIdB;
        _objectName = objectName;
        Title = UiText.T("BackupCompare_RecordsTitleFmt", displayName, backupIdA, backupIdB);
    }

    /// <summary>比較結果を読み込む（1 回）。</summary>
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        IsLoading = true;
        try
        {
            var detail = await _compare.LoadDetailAsync(_backupIdA, _backupIdB, _objectName, CancellationToken.None);
            _all.Clear();
            foreach (var diff in detail.Rows)
            {
                _all.Add(new BackupCompareRecordRowViewModel(diff));
            }

            Truncated = detail.Truncated;
            StatusMessage = UiText.T(
                "BackupCompare_RecordsSummaryFmt", detail.Added + detail.Removed + detail.Changed,
                detail.Added, detail.Removed, detail.Changed);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error($"比較結果を読み込めませんでした: {_objectName}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var tokens = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _filtered = tokens.Length == 0
            ? _all.ToList()
            : _all.Where(row => tokens.All(token => row.Blob.Contains(token, StringComparison.OrdinalIgnoreCase))).ToList();
        _pageIndex = 0;
        UpdatePage();
    }

    private void UpdatePage()
    {
        var total = _filtered.Count;
        var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
        _pageIndex = Math.Clamp(_pageIndex, 0, pageCount - 1);

        PageRows.Clear();
        foreach (var row in _filtered.Skip(_pageIndex * PageSize).Take(PageSize))
        {
            PageRows.Add(row);
        }

        HasPrev = _pageIndex > 0;
        HasNext = _pageIndex < pageCount - 1;
        PageText = total == 0
            ? UiText.T("BackupRecords_PageFmt", 0, 0, 0)
            : UiText.T("BackupRecords_PageFmt", _pageIndex * PageSize + 1, Math.Min(total, (_pageIndex + 1) * PageSize), total);
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (HasPrev)
        {
            _pageIndex--;
            UpdatePage();
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (HasNext)
        {
            _pageIndex++;
            UpdatePage();
        }
    }
}
