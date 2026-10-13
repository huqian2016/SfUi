using System.Data;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>SOQL 結果グリッドの 1 列。</summary>
public sealed record SoqlGridColumn(string Key, bool IsId, bool IsReadOnly);

/// <summary>結果グリッドの 1 セル（編集値と、組織へ未反映かどうかを持つ）。</summary>
public sealed partial class SoqlCell : ObservableObject
{
    public SoqlCell(string? value, bool isReadOnly)
    {
        OriginalValue = value;
        _value = value;
        IsReadOnly = isReadOnly;
    }

    /// <summary>読み取り専用（Id・システム項目・参照項目など）。</summary>
    public bool IsReadOnly { get; }

    /// <summary>実行時の値（反映成功で更新される比較基準）。</summary>
    public string? OriginalValue { get; private set; }

    /// <summary>現在の表示・編集値。</summary>
    [ObservableProperty]
    private string? _value;

    /// <summary>編集済みで、組織へ未反映か。</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>反映エラー メッセージ（未反映セルに表示）。</summary>
    [ObservableProperty]
    private string? _error;

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>色分け用の状態（0 = 変更なし / 1 = 未反映 / 2 = エラー）。</summary>
    public int StyleState => HasError ? 2 : IsDirty ? 1 : 0;

    partial void OnValueChanged(string? value)
    {
        Error = null;
        IsDirty = !IsReadOnly && !ValuesEqual(value, OriginalValue);
    }

    partial void OnErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(StyleState));
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(StyleState));

    /// <summary>組織への反映成功後、現在の値を確定する。</summary>
    public void Commit()
    {
        OriginalValue = Value;
        IsDirty = false;
        Error = null;
    }

    private static bool ValuesEqual(string? a, string? b) =>
        string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
}

/// <summary>結果グリッドの 1 行。</summary>
public sealed class SoqlGridRow
{
    public SoqlGridRow(string? recordId, IReadOnlyList<SoqlCell> cells)
    {
        RecordId = recordId;
        Cells = cells;
    }

    /// <summary>レコードの Id（無い場合は null）。</summary>
    public string? RecordId { get; }

    public IReadOnlyList<SoqlCell> Cells { get; }
}

/// <summary>DataTable から結果グリッドのモデルを作る。</summary>
public static class SoqlResultModelBuilder
{
    /// <summary>
    /// Id 列を先頭に固定した列と行を作る。Id が無い結果（集計など）はすべて読み取り専用。
    /// </summary>
    public static (List<SoqlGridColumn> Columns, List<SoqlGridRow> Rows) Build(DataTable table)
    {
        var names = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        var hasId = names.Any(name => string.Equals(name, "Id", StringComparison.Ordinal));

        var ordered = new List<string>(names.Count);
        if (hasId)
        {
            ordered.Add("Id");
        }

        ordered.AddRange(names.Where(name => !string.Equals(name, "Id", StringComparison.Ordinal)));

        var columns = new List<SoqlGridColumn>(ordered.Count);
        foreach (var name in ordered)
        {
            var hasJson = table.Rows.Cast<DataRow>().Any(row =>
                row[name] is not DBNull
                && SoqlEditRules.LooksLikeJson(Convert.ToString(row[name], CultureInfo.InvariantCulture)));
            var isId = string.Equals(name, "Id", StringComparison.Ordinal);
            var readOnly = !hasId || SoqlEditRules.IsReadOnlyColumn(name, hasJson);
            columns.Add(new SoqlGridColumn(name, isId, readOnly));
        }

        var idColumnIndex = columns.FindIndex(c => c.IsId);
        var rows = new List<SoqlGridRow>(table.Rows.Count);
        foreach (DataRow row in table.Rows)
        {
            var cells = new SoqlCell[ordered.Count];
            for (var i = 0; i < ordered.Count; i++)
            {
                var raw = row[ordered[i]] is DBNull ? null : Convert.ToString(row[ordered[i]], CultureInfo.InvariantCulture);
                cells[i] = new SoqlCell(raw, columns[i].IsReadOnly);
            }

            var recordId = idColumnIndex >= 0 ? cells[idColumnIndex].Value : null;
            rows.Add(new SoqlGridRow(recordId, cells));
        }

        return (columns, rows);
    }
}
