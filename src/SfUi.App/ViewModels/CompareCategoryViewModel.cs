using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>比較カテゴリ（タブ 1 つ分）の状態。比較表の適用・差分フィルタ・表示行を管理する。</summary>
public partial class CompareCategoryViewModel : ObservableObject
{
    private readonly (string Label, string Key)[] _displayColumns;

    public CompareCategoryViewModel(OrgCompareCategory category)
    {
        Category = category;
        _title = UiText.T(category.TitleKey);
        var columns = category.SectionId is null
            ? Array.Empty<OrgInfoColumn>()
            : OrgInfoSections.ColumnsFor(category.SectionId);
        _displayColumns = category.DisplayColumns
            .Select(key => (Label: columns.FirstOrDefault(c => c.Key == key)?.Label ?? key, Key: key))
            .ToArray();
    }

    /// <summary>カテゴリ定義（Core）。</summary>
    public OrgCompareCategory Category { get; }

    public string Id => Category.Id;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _summaryText;

    [ObservableProperty]
    private string? _emptyMessage;

    /// <summary>現在の比較表（未取得は null）。CSV 出力でも使用する。</summary>
    public OrgCompareTable? Table { get; private set; }

    public ObservableCollection<CompareRowViewModel> Rows { get; } = new();

    /// <summary>グリッド列（組織数）が変わったことをビューへ通知する。</summary>
    public event Action? ColumnsChanged;

    /// <summary>比較表を適用する（列構成の変更を通知）。</summary>
    public void Apply(OrgCompareTable table, bool diffOnly)
    {
        Table = table;
        ApplyFilter(diffOnly);
        ColumnsChanged?.Invoke();
    }

    /// <summary>差分のみフィルタを適用して表示行を再構築する（列は変化しない）。</summary>
    public void ApplyFilter(bool diffOnly)
    {
        Rows.Clear();
        if (Table is null)
        {
            SummaryText = null;
            EmptyMessage = null;
            return;
        }

        foreach (var row in Table.Rows.Where(r => !diffOnly || r.IsDiff))
        {
            Rows.Add(new CompareRowViewModel(row, _displayColumns));
        }

        SummaryText = UiText.T("Compare_SummaryFmt", Table.DiffCount, Table.Rows.Count);
        EmptyMessage = Rows.Count == 0 ? UiText.T("Compare_Empty") : null;
    }

    /// <summary>組織未選択などで表示を空にする。</summary>
    public void Clear()
    {
        Table = null;
        IsLoaded = false;
        Rows.Clear();
        SummaryText = null;
        EmptyMessage = null;
        ColumnsChanged?.Invoke();
    }

    /// <summary>言語切替の再ローカライズ（タイトル + 表示行の再構築）。</summary>
    public void Relocalize(bool diffOnly)
    {
        Title = UiText.T(Category.TitleKey);
        ApplyFilter(diffOnly);
    }

    /// <summary>UIA のタブ名表示用（既定の ToString だと VM 型名になるため）。</summary>
    public override string ToString() => Title;
}

/// <summary>比較グリッドの表示行。</summary>
public sealed class CompareRowViewModel
{
    public CompareRowViewModel(OrgCompareRow row, IReadOnlyList<(string Label, string Key)> displayColumns)
    {
        Key = row.Key;
        Label = row.Label;
        IsDiff = row.IsDiff;
        Cells = row.Cells.Select(cell => new CompareCellViewModel(cell, displayColumns)).ToList();
    }

    public string Key { get; }

    public string Label { get; }

    public bool IsDiff { get; }

    public IReadOnlyList<CompareCellViewModel> Cells { get; }
}

/// <summary>比較グリッドのセル表示（テキスト・ツールチップ・状態フラグ）。</summary>
public sealed class CompareCellViewModel
{
    public CompareCellViewModel(OrgCompareCell cell, IReadOnlyList<(string Label, string Key)> displayColumns)
    {
        Text = OrgCompareService.CellText(cell);
        IsMissing = cell.State == OrgCompareCellState.Missing;
        IsNotFetched = cell.State == OrgCompareCellState.NotFetched;
        IsFailed = cell.State == OrgCompareCellState.Failed;
        ToolTip = BuildToolTip(cell, displayColumns);
    }

    public string Text { get; }

    public string? ToolTip { get; }

    public bool IsMissing { get; }

    public bool IsNotFetched { get; }

    public bool IsFailed { get; }

    private static string? BuildToolTip(OrgCompareCell cell, IReadOnlyList<(string Label, string Key)> displayColumns)
    {
        if (cell.State != OrgCompareCellState.Value || displayColumns.Count == 0)
        {
            return null;
        }

        var lines = new List<string>(displayColumns.Count);
        for (var i = 0; i < displayColumns.Count && i < cell.RawValues.Count; i++)
        {
            var raw = cell.RawValues[i];
            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }

            lines.Add($"{displayColumns[i].Label}: {OrgInfoDisplay.FormatValueToken(raw)}");
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }
}
