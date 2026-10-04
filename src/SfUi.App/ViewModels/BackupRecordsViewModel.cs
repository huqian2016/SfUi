using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>レコード詳細ウィンドウの 1 行（列と同じ順序のセル配列 + 検索用テキスト）。</summary>
public sealed class BackupRecordRowViewModel
{
    public BackupRecordRowViewModel(IReadOnlyDictionary<string, string?> row, IReadOnlyList<string> columns)
    {
        Cells = columns.Select(c => row.TryGetValue(c, out var value) ? value : null).ToArray();
        Blob = string.Join('\n', Cells.Select(c => c ?? string.Empty)).ToLowerInvariant();
    }

    /// <summary>DataGrid の各列がバインドするセル（列と同じ順序）。</summary>
    public string?[] Cells { get; }

    /// <summary>AND 検索用の小文字連結テキスト。</summary>
    public string Blob { get; }
}

/// <summary>バックアップ内のレコードを表示する別ウィンドウの ViewModel（列動的・200 件/ページ・AND 検索）。</summary>
public sealed partial class BackupRecordsViewModel : ObservableObject
{
    private const int PageSize = 200;

    private readonly BackupService _backups;
    private readonly AppLog _log;
    private readonly List<BackupRecordRowViewModel> _all = new();
    private List<BackupRecordRowViewModel> _filtered = new();
    private string _backupId = string.Empty;
    private BackupObjectInfo? _info;
    private bool _loaded;

    public BackupRecordsViewModel(BackupService backups, AppLog log)
    {
        _backups = backups;
        _log = log;
    }

    public ObservableCollection<BackupRecordRowViewModel> PageRows { get; } = new();

    public IReadOnlyList<string> Columns { get; private set; } = Array.Empty<string>();

    /// <summary>列が確定したとき（ウィンドウ側で DataGrid の列を再構築する）。</summary>
    public event Action? ColumnsChanged;

    /// <summary>上限で打ち切られたときに表示する注意文。</summary>
    public string TruncatedText => UiText.T("BackupRecords_Truncated", BackupService.MaxDetailRecords);

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

    private int _pageIndex;

    public void Initialize(string backupId, BackupObjectInfo info, string displayName)
    {
        if (_loaded)
        {
            return;
        }

        _backupId = backupId;
        _info = info;
        Title = UiText.T("BackupRecords_TitleFmt", displayName, backupId);
    }

    /// <summary>レコードを読み込む（1 回）。</summary>
    public async Task LoadAsync()
    {
        if (_loaded || _info is null)
        {
            return;
        }

        _loaded = true;
        IsLoading = true;
        try
        {
            var records = await _backups.LoadRecordsAsync(_backupId, _info);
            Columns = records.Columns;
            _all.Clear();
            foreach (var row in records.Rows)
            {
                _all.Add(new BackupRecordRowViewModel(row, records.Columns));
            }

            Truncated = records.Truncated;
            StatusMessage = UiText.T("BackupRecords_SummaryFmt", _all.Count, records.Columns.Count);
            ColumnsChanged?.Invoke();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error($"バックアップのレコードを読み込めませんでした: {_backupId}", ex);
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
