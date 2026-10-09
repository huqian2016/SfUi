using System.Globalization;
using SfUi.Etl.Expressions;
using SfUi.Etl.Staging;

namespace SfUi.Etl.Transforms;

/// <summary>ターゲット項目 1 つ分のマッピング定義。</summary>
/// <param name="TargetField">ターゲット項目 API 名。</param>
/// <param name="Expression">値の式（例: <c>[Name]</c>, <c>CONCAT([姓], " ", [名])</c>, <c>"固定"</c>, <c>NOW()</c>）。</param>
/// <param name="Type">ステージング列の型。</param>
public sealed record FieldMapping(string TargetField, string Expression, StagingColumnType Type = StagingColumnType.Text);

/// <summary>
/// ソース行 → ターゲット項目値への変換器。式のコンパイルは 1 回だけ行い、行ごとは位置引数の高速パスで評価する。
/// <para>
/// <c>PREV("列名")</c> と <c>ROW_NUMBER()</c> をこのマッパーが提供する（指定された <see cref="ExpressionEngine"/> の
/// <see cref="ExpressionHost"/> の該当プロバイダを上書きする。複数ステップではステップごとにエンジンを作る前提）。
/// </para>
/// </summary>
public sealed class RowMapper
{
    private readonly Dictionary<string, int> _sourceIndex;
    private readonly CompiledExpression[] _expressions;
    private readonly int[][] _argIndexes;
    private readonly ExpressionHost _host;
    private Dictionary<string, object?> _prev = new(StringComparer.Ordinal);
    private int _rowNumber;

    public RowMapper(
        IReadOnlyList<string> sourceColumns,
        IReadOnlyList<FieldMapping> mappings,
        ExpressionEngine engine)
    {
        SourceColumns = sourceColumns;
        Mappings = mappings;
        _host = engine.Host;

        _sourceIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < sourceColumns.Count; i++)
        {
            _sourceIndex[sourceColumns[i]] = i;
        }

        _expressions = new CompiledExpression[mappings.Count];
        _argIndexes = new int[mappings.Count][];
        var stagingColumns = new List<StagingColumn>(mappings.Count);

        for (var m = 0; m < mappings.Count; m++)
        {
            var compiled = engine.Compile(mappings[m].Expression, sourceColumns);
            _expressions[m] = compiled;

            var indexes = new int[compiled.Variables.Count];
            for (var v = 0; v < indexes.Length; v++)
            {
                indexes[v] = _sourceIndex.TryGetValue(compiled.Variables[v], out var index) ? index : -1;
            }

            _argIndexes[m] = indexes;
            stagingColumns.Add(new StagingColumn(mappings[m].TargetField, mappings[m].Type));
        }

        StagingColumns = stagingColumns;

        // PREV / ROW_NUMBER のプロバイダを提供
        _host.Prev = field => _prev.TryGetValue(field, out var value) ? value : null;
        _host.RowNumberProvider = () => _rowNumber;
    }

    /// <summary>ソース列名（式から参照可能な名前）。</summary>
    public IReadOnlyList<string> SourceColumns { get; }

    /// <summary>マッピング定義。</summary>
    public IReadOnlyList<FieldMapping> Mappings { get; }

    /// <summary>ステージング テーブル用の列定義（<see cref="RunStagingStore.CreateStagingTable"/> に渡す）。</summary>
    public IReadOnlyList<StagingColumn> StagingColumns { get; }

    /// <summary>1 行を変換する（式評価 → 型変換）。</summary>
    public object?[] MapRow(object?[] sourceRow)
    {
        _rowNumber++;
        var result = new object?[Mappings.Count];

        for (var m = 0; m < _expressions.Length; m++)
        {
            var indexes = _argIndexes[m];
            var args = new object?[indexes.Length];
            for (var v = 0; v < indexes.Length; v++)
            {
                var i = indexes[v];
                args[v] = i >= 0 && i < sourceRow.Length ? sourceRow[i] : null;
            }

            result[m] = ConvertValue(_expressions[m].EvaluateArgs(args), Mappings[m].Type);
        }

        // 次行の PREV() 用にソース値を保存
        var prev = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < SourceColumns.Count && i < sourceRow.Length; i++)
        {
            prev[SourceColumns[i]] = sourceRow[i];
        }

        _prev = prev;
        return result;
    }

    /// <summary>行シーケンスをまとめて変換する。</summary>
    public IEnumerable<object?[]> MapAll(IEnumerable<object?[]> rows)
    {
        foreach (var row in rows)
        {
            yield return MapRow(row);
        }
    }

    /// <summary>式の評価結果を宣言型へ変換する（変換不能 → null）。</summary>
    public static object? ConvertValue(object? value, StagingColumnType type) => type switch
    {
        StagingColumnType.Text => value is null ? null : ExprFunctions.Text(value),
        StagingColumnType.Integer => ToInteger(value),
        StagingColumnType.Real => ToReal(value),
        StagingColumnType.Boolean => ToBoolean(value),
        StagingColumnType.DateTime => ExprFunctions.ToDate(value),
        _ => value,
    };

    private static object? ToInteger(object? value)
        => ExprFunctions.ToNumber(value) is decimal d ? (long)Math.Round(d, MidpointRounding.AwayFromZero) : null;

    private static object? ToReal(object? value)
        => ExprFunctions.ToNumber(value) is decimal d ? (double)d : null;

    private static object? ToBoolean(object? value)
    {
        if (value is null) return null;
        if (value is bool b) return b;

        var s = (ExprFunctions.Text(value) as string)?.Trim();
        if (string.IsNullOrEmpty(s)) return null;

        switch (s.ToLowerInvariant())
        {
            case "true":
            case "1":
            case "y":
            case "yes":
                return true;
            case "false":
            case "0":
            case "n":
            case "no":
                return false;
        }

        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d != 0 : null;
    }
}
