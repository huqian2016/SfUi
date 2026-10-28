namespace SfUi.Core;

/// <summary>レポートのフィルタ演算子。</summary>
public static class ReportOperators
{
    public const string Equals = "eq";
    public const string NotEquals = "ne";
    public const string LessThan = "lt";
    public const string LessThanOrEqual = "lte";
    public const string GreaterThan = "gt";
    public const string GreaterThanOrEqual = "gte";
    public const string Contains = "contains";
    public const string StartsWith = "startsWith";
    public const string EndsWith = "endsWith";
    public const string In = "in";
    public const string NotIn = "notIn";
    public const string IsNull = "isNull";
    public const string IsNotNull = "isNotNull";
    public const string DateLiteral = "dateLiteral";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Equals, NotEquals, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual,
        Contains, StartsWith, EndsWith, In, NotIn, IsNull, IsNotNull, DateLiteral,
    };

    /// <summary>値の入力が必要か。</summary>
    public static bool NeedsValue(string? @operator) => @operator is not (IsNull or IsNotNull);

    /// <summary>画面表示用のラベル（比較演算子は記号、それ以外はローカライズ）。</summary>
    public static string ToLabel(string? @operator) => @operator switch
    {
        Equals => "=",
        NotEquals => "≠",
        LessThan => "<",
        LessThanOrEqual => "≤",
        GreaterThan => ">",
        GreaterThanOrEqual => "≥",
        Contains => UiText.T("Report_OpContains"),
        StartsWith => UiText.T("Report_OpStartsWith"),
        EndsWith => UiText.T("Report_OpEndsWith"),
        In => UiText.T("Report_OpIn"),
        NotIn => UiText.T("Report_OpNotIn"),
        IsNull => UiText.T("Report_OpIsNull"),
        IsNotNull => UiText.T("Report_OpIsNotNull"),
        DateLiteral => UiText.T("Report_OpDateLiteral"),
        _ => @operator ?? "",
    };
}

/// <summary>集計関数。</summary>
public static class ReportAggregateFunctions
{
    public const string Sum = "sum";
    public const string Average = "avg";
    public const string Min = "min";
    public const string Max = "max";
    public const string Count = "count";
    public const string CountDistinct = "countDistinct";

    public static readonly IReadOnlyList<string> All = new[] { Sum, Average, Min, Max, Count, CountDistinct };

    public static string ToLabel(string? function) => function switch
    {
        Sum => UiText.T("Report_AggSum"),
        Average => UiText.T("Report_AggAvg"),
        Min => UiText.T("Report_AggMin"),
        Max => UiText.T("Report_AggMax"),
        Count => UiText.T("Report_AggCount"),
        CountDistinct => UiText.T("Report_AggCountDistinct"),
        _ => function ?? "",
    };

    /// <summary>数値の集計（件数は <paramref name="values"/> の要素数、CountDistinct は呼び出し側で渡す）。</summary>
    public static decimal Apply(string function, IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        return function switch
        {
            Average => values.Sum() / values.Count,
            Min => values.Min(),
            Max => values.Max(),
            Count => values.Count,
            _ => values.Sum(),
        };
    }
}

/// <summary>レポートの表示形式。</summary>
public static class ReportDisplayModes
{
    public const string Tabular = "tabular";
    public const string Summary = "summary";
    public const string Matrix = "matrix";

    public static readonly IReadOnlyList<string> All = new[] { Tabular, Summary, Matrix };

    public static string ToLabel(string? mode) => mode switch
    {
        Summary => UiText.T("Report_ModeSummary"),
        Matrix => UiText.T("Report_ModeMatrix"),
        _ => UiText.T("Report_ModeTabular"),
    };
}

/// <summary>セル値の表示書式。</summary>
public static class ReportValueFormats
{
    public const string Auto = "auto";
    public const string Text = "text";
    public const string Number = "number";
    public const string Currency = "currency";
    public const string Percent = "percent";
    public const string Date = "date";
    public const string DateTime = "datetime";
    public const string Boolean = "bool";
    public const string Url = "url";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Auto, Text, Number, Currency, Percent, Date, DateTime, Boolean, Url,
    };

    public static string ToLabel(string? format) => format switch
    {
        Text => UiText.T("Report_FormatText"),
        Number => UiText.T("Report_FormatNumber"),
        Currency => UiText.T("Report_FormatCurrency"),
        Percent => UiText.T("Report_FormatPercent"),
        Date => UiText.T("Report_FormatDate"),
        DateTime => UiText.T("Report_FormatDateTime"),
        Boolean => UiText.T("Report_FormatBool"),
        Url => UiText.T("Report_FormatUrl"),
        _ => UiText.T("Report_FormatAuto"),
    };
}

/// <summary>コンボボックス用の選択肢。</summary>
public sealed record ReportChoice(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>レポートの 1 列。</summary>
public sealed class ReportColumn
{
    /// <summary>項目 API 名（リレーションは "Account.Name" 形式）。</summary>
    public string Field { get; set; } = "";

    /// <summary>表示ラベル（空 = 項目 API 名の末尾）。</summary>
    public string? Label { get; set; }

    public bool Hidden { get; set; }

    /// <summary><see cref="ReportValueFormats"/> のいずれか。</summary>
    public string Format { get; set; } = ReportValueFormats.Auto;

    public int? Width { get; set; }
}

/// <summary>レポートのフィルタ条件。</summary>
public sealed class ReportFilter
{
    public string Field { get; set; } = "";

    /// <summary><see cref="ReportOperators"/> のいずれか。</summary>
    public string Operator { get; set; } = ReportOperators.Equals;

    public string? Value { get; set; }

    public bool IncludeNulls { get; set; }
}

/// <summary>並べ替え。</summary>
public sealed class ReportSort
{
    public string Field { get; set; } = "";

    /// <summary>asc / desc。</summary>
    public string Direction { get; set; } = "asc";

    public bool Descending => string.Equals(Direction, "desc", StringComparison.OrdinalIgnoreCase);
}

/// <summary>グループ化（最大 3 階層）。</summary>
public sealed class ReportGrouping
{
    public string Field { get; set; } = "";

    public string Direction { get; set; } = "asc";

    /// <summary>日付項目の粒度（空 / day / week / month / quarter / year）。</summary>
    public string? DateGranularity { get; set; }
}

/// <summary>集計（合計・平均など）。</summary>
public sealed class ReportAggregate
{
    public string Field { get; set; } = "";

    /// <summary><see cref="ReportAggregateFunctions"/> のいずれか。</summary>
    public string Function { get; set; } = ReportAggregateFunctions.Sum;

    public string? Label { get; set; }
}

/// <summary>ローカルに保存するレポート定義。</summary>
public sealed class ReportDefinition
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>対象オブジェクトの API 名。</summary>
    public string ObjectName { get; set; } = "Account";

    /// <summary>対象組織（空 = 実行時に選択中の組織）。</summary>
    public string? TargetOrg { get; set; }

    public List<ReportColumn> Columns { get; set; } = new();

    public List<ReportFilter> Filters { get; set; } = new();

    /// <summary>複数条件の結合方法（and / or）。</summary>
    public string FilterLogic { get; set; } = "and";

    public List<ReportSort> Sorts { get; set; } = new();

    public List<ReportGrouping> Groupings { get; set; } = new();

    public List<ReportAggregate> Aggregates { get; set; } = new();

    /// <summary>取得する最大行数（0 = 制限なし）。</summary>
    public int RowLimit { get; set; } = 2000;

    /// <summary>Bulk API を使うか（大量行向け）。</summary>
    public bool UseBulk { get; set; }

    /// <summary>リアルタイム更新の間隔（秒）。0 = オフ。</summary>
    public int RefreshSeconds { get; set; }

    /// <summary><see cref="ReportDisplayModes"/> のいずれか。</summary>
    public string DisplayMode { get; set; } = ReportDisplayModes.Tabular;

    /// <summary>グラフ設定（未設定ならグラフなし）。</summary>
    public ReportChartDefinition? Chart { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? ObjectName : Name;

    public static string NewId() => "r_" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>表示対象の列（空なら Id / Name）。</summary>
    public IReadOnlyList<ReportColumn> VisibleColumns
    {
        get
        {
            var visible = Columns.Where(c => !c.Hidden && !string.IsNullOrWhiteSpace(c.Field)).ToList();
            if (visible.Count > 0)
            {
                return visible;
            }

            return new List<ReportColumn>
            {
                new() { Field = "Id" },
                new() { Field = "Name" },
            };
        }
    }
}

/// <summary>集計値 1 件（グループ行 / 合計行の表示用）。</summary>
public sealed record ReportMetric(string Label, string Function, string Text, decimal Value);

/// <summary>グループ 1 件（キーと集計値）。</summary>
public sealed class ReportGroup
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public int RowCount { get; init; }

    public IReadOnlyList<ReportMetric> Metrics { get; init; } = Array.Empty<ReportMetric>();
}

/// <summary>ローカル レポートの実行結果。</summary>
public sealed class LocalReportResult
{
    public required string ReportId { get; init; }

    public required string ReportName { get; init; }

    public required string ObjectName { get; init; }

    public string? Org { get; init; }

    public required string Soql { get; init; }

    public required ReportTable Table { get; init; }

    /// <summary>グループ集計（グループ化が未指定なら空）。</summary>
    public IReadOnlyList<ReportGroup> Groups { get; init; } = Array.Empty<ReportGroup>();

    /// <summary>全体の集計（件数 + 集計関数）。</summary>
    public ReportGroup? Totals { get; init; }

    /// <summary>行数上限で切り捨てたか。</summary>
    public bool Truncated { get; init; }

    public required string Engine { get; init; }

    public TimeSpan Duration { get; init; }

    public DateTimeOffset RanAt { get; init; } = DateTimeOffset.Now;

    public int RowCount => Table.RowCount;

    /// <summary>レポート出力（ReportDocument）へ変換する。</summary>
    public ReportDocument ToDocument()
    {
        var document = new ReportDocument
        {
            Title = ReportName,
            Subtitle = ObjectName,
            Org = Org,
            Source = "reports",
            GeneratedAt = RanAt,
        };

        var meta = new List<KeyValuePair<string, string>>
        {
            new(UiText.T("Report_MetaTitle"), ReportName),
            new(UiText.T("Report_ColObject"), ObjectName),
            new(UiText.T("Report_Rows"), RowCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)),
        };
        if (!string.IsNullOrWhiteSpace(Org))
        {
            meta.Insert(2, new KeyValuePair<string, string>(UiText.T("Report_MetaOrg"), Org!));
        }

        if (Truncated)
        {
            meta.Add(new KeyValuePair<string, string>(UiText.T("Report_Truncated"), UiText.T("Report_TruncatedYes")));
        }

        document.Sections.Add(new ReportSection
        {
            Heading = UiText.T("Report_SummaryHeading"),
            KeyValues = meta,
            Text = Soql,
        });

        document.Sections.Add(ReportSection.FromTable(Table, UiText.T("Report_DetailHeading")));

        if (Groups.Count > 0)
        {
            var columns = new List<string> { UiText.T("Report_GroupHeading"), UiText.T("Report_Rows") };
            columns.AddRange(Groups[0].Metrics.Select(m => m.Label));
            var rows = new List<IReadOnlyList<string?>>();
            foreach (var group in Groups)
            {
                var row = new List<string?> { group.Label, group.RowCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) };
                row.AddRange(group.Metrics.Select(m => m.Text));
                rows.Add(row);
            }

            if (Totals is not null)
            {
                var row = new List<string?> { Totals.Label, Totals.RowCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) };
                row.AddRange(Totals.Metrics.Select(m => m.Text));
                rows.Add(row);
            }

            document.Sections.Add(ReportSection.FromTable(
                new ReportTable { Name = UiText.T("Report_GroupHeading"), Columns = columns, Rows = rows },
                UiText.T("Report_GroupHeading")));
        }

        return document;
    }
}
