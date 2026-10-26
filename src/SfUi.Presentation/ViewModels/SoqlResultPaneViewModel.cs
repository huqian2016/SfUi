using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// 複数 SOQL（<c>;</c> 区切り）を実行したときの結果枠 1 つ分の ViewModel。
/// 枠ごとに絞り込み・状態表示・読み取り専用グリッドを持つ。
/// </summary>
public sealed partial class SoqlResultPaneViewModel : ObservableObject
{
    private readonly Func<SoqlGridRow, Task>? _openRecord;

    public SoqlResultPaneViewModel(int index, string query, Func<SoqlGridRow, Task>? openRecord = null)
    {
        Index = index;
        Query = query;
        _openRecord = openRecord;
    }

    /// <summary>スクリプト内の位置（0 始まり）。</summary>
    public int Index { get; }

    /// <summary>この枠が実行した SOQL（コメント除去済み）。</summary>
    public string Query { get; }

    public string Title => UiText.T("Soql_ResultPaneFmt", Index + 1, Summarize(Query));

    public ObservableCollection<SoqlGridColumn> Columns { get; } = new();

    /// <summary>結果の全行。</summary>
    public ObservableCollection<SoqlGridRow> Rows { get; } = new();

    /// <summary>絞り込み後の表示行。</summary>
    public ObservableCollection<SoqlGridRow> VisibleRows { get; } = new();

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _filterLabel = UiText.T("Soql_PaneFilter");

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private bool _isCachedResult;

    [ObservableProperty]
    private string _cachedResultText = string.Empty;

    /// <summary>表示中の行数 / 全行数（例: 1,234 / 2,000 行）。</summary>
    public string CountText => UiText.T("Soql_PaneCountFmt", VisibleRows.Count, Rows.Count);

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>結果を流し込む（最新の実行結果）。</summary>
    public void SetResult(DataTable table, string status)
    {
        Bind(table);
        StatusText = status;
        IsCachedResult = false;
        CachedResultText = string.Empty;
        ErrorText = null;
        IsRunning = false;
    }

    /// <summary>キャッシュした結果を流し込む（裏で最新を取得中）。</summary>
    public void SetCached(DataTable table, DateTimeOffset cachedAt)
    {
        Bind(table);
        var time = cachedAt.LocalDateTime.ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture);
        IsCachedResult = true;
        CachedResultText = UiText.T("Soql_CacheBadgeFmt", time);
        StatusText = UiText.T("Soql_CacheRefreshingFmt", time);
        ErrorText = null;
        IsRunning = false;
    }

    /// <summary>エラーを表示する（キャッシュ表示中なら行はそのまま残す）。</summary>
    public void SetError(string message)
    {
        ErrorText = message;
        StatusText = IsCachedResult
            ? UiText.T("Soql_CacheRefreshFailedFmt", message, CachedResultText)
            : UiText.T("Common_FailedFmt", message);
        IsRunning = false;
    }

    public void SetRunning()
    {
        IsRunning = true;
        StatusText = UiText.T("Soql_PaneRunning");
        ErrorText = null;
        IsCachedResult = false;
        CachedResultText = string.Empty;
    }

    public void Clear()
    {
        Columns.Clear();
        Rows.Clear();
        VisibleRows.Clear();
        StatusText = string.Empty;
        ErrorText = null;
        IsCachedResult = false;
        CachedResultText = string.Empty;
        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>絞り込み後の一覧を DataTable にする（コピー / 出力用）。</summary>
    public DataTable? ToFilteredTable()
    {
        if (Columns.Count == 0)
        {
            return null;
        }

        var table = new DataTable();
        foreach (var column in Columns)
        {
            table.Columns.Add(column.Key, typeof(string));
        }

        foreach (var row in VisibleRows)
        {
            var values = table.NewRow();
            for (var i = 0; i < Columns.Count && i < row.Cells.Count; i++)
            {
                values[i] = (object?)row.Cells[i].Value ?? DBNull.Value;
            }

            table.Rows.Add(values);
        }

        return table;
    }

    [RelayCommand]
    private Task OpenRecordAsync(SoqlGridRow? row) =>
        row is null || _openRecord is null ? Task.CompletedTask : _openRecord(row);

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void Bind(DataTable table)
    {
        Columns.Clear();
        Rows.Clear();
        var (columns, rows) = SoqlResultModelBuilder.Build(table);
        foreach (var column in columns)
        {
            Columns.Add(column);
        }

        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = FilterText.Trim();
        VisibleRows.Clear();
        foreach (var row in Rows)
        {
            if (filter.Length == 0 || Matches(row, filter))
            {
                VisibleRows.Add(row);
            }
        }

        OnPropertyChanged(nameof(CountText));
    }

    private static bool Matches(SoqlGridRow row, string filter)
    {
        foreach (var cell in row.Cells)
        {
            if (cell.Value is { } value && value.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return row.RecordId?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>枠のタイトル用にクエリを 1 行に短縮する。</summary>
    private static string Summarize(string query)
    {
        var text = string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 70 ? text : text[..67] + "…";
    }
}
