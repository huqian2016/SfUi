using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>レコード単位比較ウィンドウの 1 行。</summary>
public sealed class BackupCompareRecordRowViewModel
{
    private const int MaxFieldsShown = 5;
    private const int MaxValueLength = 40;

    public BackupCompareRecordRowViewModel(BackupRecordDiff diff, string? linkUrl)
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
        LinkUrl = linkUrl ?? string.Empty;
        ChangeSummary = BuildSummary(diff);
        Blob = $"{KindText}\n{Id}\n{Display}\n{ChangeSummary}".ToLowerInvariant();
    }

    public string KindText { get; }

    public bool IsAdded { get; }

    public bool IsRemoved { get; }

    public string Id { get; }

    public string Display { get; }

    /// <summary>Salesforce のレコードページ URL（解決できない場合は空）。</summary>
    public string LinkUrl { get; }

    public bool HasLink => !string.IsNullOrEmpty(LinkUrl);

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
    private readonly OrgService _orgs;
    private readonly AppLog _log;
    private readonly List<BackupCompareRecordRowViewModel> _all = new();
    private List<BackupCompareRecordRowViewModel> _filtered = new();
    private string _backupIdA = string.Empty;
    private string _backupIdB = string.Empty;
    private string _objectName = string.Empty;
    private OrgInfo? _currentOrg;
    private bool _loaded;
    private int _pageIndex;

    public BackupCompareRecordsViewModel(BackupCompareService compare, OrgService orgs, AppLog log)
    {
        _compare = compare;
        _orgs = orgs;
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

    public void Initialize(string backupIdA, string backupIdB, string objectName, string displayName, OrgInfo? currentOrg = null)
    {
        if (_loaded)
        {
            return;
        }

        _backupIdA = backupIdA;
        _backupIdB = backupIdB;
        _objectName = objectName;
        _currentOrg = currentOrg;
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
            var (urlA, urlB) = await ResolveUrlsAsync();
            _all.Clear();
            foreach (var diff in detail.Rows)
            {
                // 削除行は A、追加 / 変更行は B のレコードページを開く（無い場合は他方で代替）
                var baseUrl = diff.Kind == BackupDiffKind.Removed ? urlA ?? urlB : urlB ?? urlA;
                var link = BackupOrgUrls.BuildRecordUrl(baseUrl, _objectName, string.IsNullOrEmpty(diff.Id) ? null : diff.Id);
                _all.Add(new BackupCompareRecordRowViewModel(diff, link));
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

    /// <summary>バックアップ元（A / B）組織のインスタンス URL を解決する（レコードページ リンク用）。</summary>
    private async Task<(string? UrlA, string? UrlB)> ResolveUrlsAsync()
    {
        try
        {
            var metadata = _compare.ListBackupMetadata();
            var metaA = metadata.FirstOrDefault(m => string.Equals(m.Id, _backupIdA, StringComparison.Ordinal));
            var metaB = metadata.FirstOrDefault(m => string.Equals(m.Id, _backupIdB, StringComparison.Ordinal));
            // 同一組織なら sf CLI を呼ばずに即返す（別組織のときだけ認証済み組織一覧を照会）
            var urlA = BackupOrgUrls.Resolve(_currentOrg, metaA?.OrgUsername, metaA?.OrgId, Array.Empty<OrgInfo>());
            var urlB = BackupOrgUrls.Resolve(_currentOrg, metaB?.OrgUsername, metaB?.OrgId, Array.Empty<OrgInfo>());
            if (!string.IsNullOrEmpty(urlA) && !string.IsNullOrEmpty(urlB))
            {
                return (urlA, urlB);
            }

            var orgs = await _orgs.ListOrgsAsync();
            return (
                urlA ?? BackupOrgUrls.Resolve(_currentOrg, metaA?.OrgUsername, metaA?.OrgId, orgs),
                urlB ?? BackupOrgUrls.Resolve(_currentOrg, metaB?.OrgUsername, metaB?.OrgId, orgs));
        }
        catch (Exception ex)
        {
            _log.Warn($"レコードページ用のインスタンス URL を解決できませんでした: {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>Salesforce のレコードページをブラウザーで開く。</summary>
    [RelayCommand]
    private void OpenRecord(BackupCompareRecordRowViewModel? row)
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
            _log.Error($"レコードページを開けませんでした: {row.LinkUrl}", ex);
        }
    }

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
