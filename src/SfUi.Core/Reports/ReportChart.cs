using System.Globalization;

namespace SfUi.Core;

/// <summary>レポートのグラフ種別。</summary>
public static class ReportChartTypes
{
    public const string None = "none";
    public const string Column = "column";
    public const string Bar = "bar";
    public const string Line = "line";
    public const string Pie = "pie";
    public const string Donut = "donut";

    public static IReadOnlyList<string> All { get; } = new[] { None, Column, Bar, Line, Pie, Donut };

    /// <summary>1 つの値系列だけを使う種別か。</summary>
    public static bool IsSingleSeries(string? type) =>
        string.Equals(type, Pie, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, Donut, StringComparison.OrdinalIgnoreCase);

    public static bool IsCircular(string? type) =>
        string.Equals(type, Pie, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, Donut, StringComparison.OrdinalIgnoreCase);

    /// <summary>表示名（ローカライズ済み）。</summary>
    public static string ToLabel(string? type) => (type ?? None) switch
    {
        Column => UiText.T("Report_ChartColumn"),
        Bar => UiText.T("Report_ChartBar"),
        Line => UiText.T("Report_ChartLine"),
        Pie => UiText.T("Report_ChartPie"),
        Donut => UiText.T("Report_ChartDonut"),
        _ => UiText.T("Report_ChartNone"),
    };

    public static ReportChoice ToChoice(string type) => new(type, ToLabel(type));
}

/// <summary>レポート定義に保存するグラフ設定。</summary>
public sealed class ReportChartDefinition
{
    /// <summary><see cref="ReportChartTypes"/> のいずれか。</summary>
    public string Type { get; set; } = ReportChartTypes.None;

    /// <summary>カテゴリ（軸）に使う列ラベル。空なら先頭の列。</summary>
    public string? CategoryField { get; set; }

    /// <summary>値に使う列ラベル（複数可）。空なら数値列を自動で使う。</summary>
    public List<string> ValueFields { get; set; } = new();

    /// <summary>グラフのタイトル（空ならレポート名）。</summary>
    public string? Title { get; set; }

    public bool ShowLegend { get; set; } = true;

    /// <summary>上位 N 件（0 = 全件）。</summary>
    public int TopN { get; set; } = 10;

    /// <summary>グラフを描くか。</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Type) &&
        !string.Equals(Type, ReportChartTypes.None, StringComparison.OrdinalIgnoreCase);

    public ReportChartDefinition Clone() => new()
    {
        Type = Type,
        CategoryField = CategoryField,
        ValueFields = ValueFields.ToList(),
        Title = Title,
        ShowLegend = ShowLegend,
        TopN = TopN,
    };
}

/// <summary>グラフの 1 系列。</summary>
public sealed class ReportChartSeries
{
    public required string Name { get; init; }

    public required IReadOnlyList<double> Values { get; init; }

    public double Max => Values.Count == 0 ? 0 : Values.Max();

    public double Sum => Values.Sum();
}

/// <summary>グラフに描くデータ（カテゴリ + 系列）。</summary>
public sealed class ReportChartData
{
    public required string Type { get; init; }

    public required IReadOnlyList<string> Categories { get; init; }

    public required IReadOnlyList<ReportChartSeries> Series { get; init; }

    public string Title { get; init; } = "";

    public bool ShowLegend { get; init; } = true;

    public bool IsEmpty => Categories.Count == 0 || Series.Count == 0 ||
        Series.All(series => series.Values.All(value => value == 0));

    /// <summary>全系列の最大値（0 以上）。</summary>
    public double MaxValue => Series.Count == 0 ? 0 : Series.Max(s => s.Max);

    /// <summary>全系列の最小値（負値がある場合のみ意味を持つ）。</summary>
    public double MinValue => Series.Count == 0 ? 0 : Series.Min(s => s.Values.Count == 0 ? 0 : s.Values.Min());

    /// <summary>
    /// 表からグラフ データを組み立てる（同じカテゴリは合計にまとめる）。
    /// グラフが無効 / 数値列が 1 つも無い場合は null。
    /// </summary>
    public static ReportChartData? Build(ReportTable? table, ReportChartDefinition? definition, string fallbackTitle = "")
    {
        if (table is null || definition is null || !definition.IsEnabled || table.Columns.Count == 0)
        {
            return null;
        }

        var categoryIndex = FindIndex(table.Columns, definition.CategoryField);
        if (categoryIndex < 0)
        {
            categoryIndex = 0;
        }

        var valueIndexes = ResolveValueIndexes(table, definition, categoryIndex);
        if (valueIndexes.Count == 0)
        {
            return null;
        }

        // カテゴリごとに合計する（出現順を保つ）
        var order = new List<string>();
        var buckets = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var row in table.Rows)
        {
            var category = ValueAt(row, categoryIndex);
            if (string.IsNullOrWhiteSpace(category))
            {
                category = "-";
            }

            if (!buckets.TryGetValue(category, out var values))
            {
                values = new double[valueIndexes.Count];
                buckets[category] = values;
                order.Add(category);
            }

            for (var i = 0; i < valueIndexes.Count; i++)
            {
                values[i] += ValueAt(row, valueIndexes[i]) is { } text && TryParse(text, out var number) ? number : 0;
            }
        }

        var categories = new List<string>(order);
        var matrices = order.Select(o => buckets[o]).ToList();
        var seriesNames = valueIndexes.Select(i => table.Columns[i]).ToList();

        // 上位 N 件（Line は出現順のまま、それ以外は値の大きい順）
        if (!string.Equals(definition.Type, ReportChartTypes.Line, StringComparison.OrdinalIgnoreCase))
        {
            var keep = Enumerable.Range(0, categories.Count)
                .OrderByDescending(i => matrices[i].Sum())
                .ToList();
            if (definition.TopN > 0)
            {
                keep = keep.Take(definition.TopN).ToList();
            }

            categories = keep.Select(i => categories[i]).ToList();
            matrices = keep.Select(i => matrices[i]).ToList();
        }

        var series = new List<ReportChartSeries>();
        for (var s = 0; s < seriesNames.Count; s++)
        {
            series.Add(new ReportChartSeries
            {
                Name = seriesNames[s],
                Values = matrices.Select(values => values[s]).ToList(),
            });
        }

        if (ReportChartTypes.IsSingleSeries(definition.Type) && series.Count > 1)
        {
            series = series.Take(1).ToList();
        }

        var data = new ReportChartData
        {
            Type = definition.Type,
            Categories = categories,
            Series = series,
            Title = string.IsNullOrWhiteSpace(definition.Title) ? fallbackTitle : definition.Title!.Trim(),
            ShowLegend = definition.ShowLegend,
        };

        return data.IsEmpty ? null : data;
    }

    /// <summary>数値列（無ければ最後の数値として読める列）を選ぶ。</summary>
    private static List<int> ResolveValueIndexes(ReportTable table, ReportChartDefinition definition, int categoryIndex)
    {
        var indexes = new List<int>();
        foreach (var field in definition.ValueFields)
        {
            var index = FindIndex(table.Columns, field);
            if (index >= 0 && index != categoryIndex && !indexes.Contains(index))
            {
                indexes.Add(index);
            }
        }

        if (indexes.Count > 0)
        {
            return indexes;
        }

        indexes.AddRange(table.NumericColumns.Where(i => i != categoryIndex).OrderBy(i => i));
        if (indexes.Count > 0)
        {
            return indexes;
        }

        for (var i = 0; i < table.Columns.Count; i++)
        {
            if (i == categoryIndex)
            {
                continue;
            }

            var numeric = table.Rows.Take(50).Count(row => ValueAt(row, i) is { } text && TryParse(text, out _));
            if (numeric > 0)
            {
                indexes.Add(i);
            }
        }

        return indexes;
    }

    private static int FindIndex(IReadOnlyList<string> columns, string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return -1;
        }

        for (var i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i], label, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string? ValueAt(IReadOnlyList<string?> row, int index) =>
        index >= 0 && index < row.Count ? row[index] : null;

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text.Replace(",", "", StringComparison.Ordinal).Replace("%", "", StringComparison.Ordinal),
            NumberStyles.Any, CultureInfo.InvariantCulture, out value);
}
