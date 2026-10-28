using System.Globalization;
using System.Text;

namespace SfUi.Core;

/// <summary>
/// ローカル レポート定義から SOQL を組み立てる（純粋関数・ネットワーク不要）。
/// </summary>
public static class ReportQueryBuilder
{
    /// <summary>取得行数の上限（0 = 無制限）。</summary>
    public const int MaxRowLimit = 200000;

    /// <summary>定義から SOQL を組み立てる。</summary>
    public static string Build(ReportDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var fields = CollectFields(definition);
        var builder = new StringBuilder();
        builder.Append("SELECT ").Append(string.Join(", ", fields));
        builder.Append(" FROM ").Append(definition.ObjectName.Trim());

        var where = BuildWhere(definition.Filters, definition.FilterLogic, out _);
        if (where.Length > 0)
        {
            builder.Append(" WHERE ").Append(where);
        }

        var order = BuildOrderBy(definition);
        if (order.Length > 0)
        {
            builder.Append(" ORDER BY ").Append(order);
        }

        if (definition.RowLimit > 0)
        {
            builder.Append(" LIMIT ").Append((definition.RowLimit + 1).ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>SELECT に含める項目（表示列 + グループ + 集計 + 並べ替え）を重複なく列挙する。</summary>
    public static IReadOnlyList<string> CollectFields(ReportDefinition definition)
    {
        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? field)
        {
            var trimmed = (field ?? "").Trim();
            if (trimmed.Length == 0 || !seen.Add(trimmed))
            {
                return;
            }

            fields.Add(trimmed);
        }

        // Id は差分検出と Salesforce レポート風のリンクに使うため常に含める
        Add("Id");
        foreach (var column in definition.Columns)
        {
            Add(column.Field);
        }

        foreach (var grouping in definition.Groupings)
        {
            Add(grouping.Field);
        }

        foreach (var aggregate in definition.Aggregates)
        {
            Add(aggregate.Field);
        }

        foreach (var sort in definition.Sorts)
        {
            Add(sort.Field);
        }

        return fields;
    }

    /// <summary>定義の検証。問題がなければ空。</summary>
    public static IReadOnlyList<string> Validate(ReportDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.ObjectName))
        {
            errors.Add(UiText.T("Report_ErrorNoObject"));
        }

        if (definition.Columns.Count(c => !c.Hidden && !string.IsNullOrWhiteSpace(c.Field)) == 0)
        {
            errors.Add(UiText.T("Report_ErrorNoColumns"));
        }

        if (definition.Filters.Any(f => string.IsNullOrWhiteSpace(f.Field)))
        {
            errors.Add(UiText.T("Report_ErrorFilterField"));
        }

        if (definition.Filters.Any(f => ReportOperators.NeedsValue(f.Operator) && string.IsNullOrWhiteSpace(f.Value)))
        {
            errors.Add(UiText.T("Report_ErrorFilterValue"));
        }

        if (definition.Groupings.Count(g => !string.IsNullOrWhiteSpace(g.Field)) > 3)
        {
            errors.Add(UiText.T("Report_ErrorTooManyGroups"));
        }

        if (definition.Groupings.Any(g => string.IsNullOrWhiteSpace(g.Field)))
        {
            errors.Add(UiText.T("Report_ErrorGroupingField"));
        }

        if (definition.Aggregates.Any(a => string.IsNullOrWhiteSpace(a.Field)))
        {
            errors.Add(UiText.T("Report_ErrorAggregateField"));
        }

        if (definition.RowLimit < 0 || definition.RowLimit > MaxRowLimit)
        {
            errors.Add(UiText.T("Report_ErrorRowLimit", MaxRowLimit.ToString("N0", CultureInfo.CurrentCulture)));
        }

        if (definition.RefreshSeconds is < 0 or 1)
        {
            errors.Add(UiText.T("Report_ErrorRefresh", ReportLivePolicy.MinRefreshSeconds.ToString(CultureInfo.CurrentCulture)));
        }

        return errors;
    }

    /// <summary>WHERE 句を組み立てる（空 = 条件なし）。</summary>
    public static string BuildWhere(IReadOnlyList<ReportFilter> filters, string? logic, out bool used)
    {
        used = false;
        var clauses = new List<string>();
        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter.Field))
            {
                continue;
            }

            var clause = BuildClause(filter);
            if (clause.Length > 0)
            {
                clauses.Add(clause);
            }
        }

        if (clauses.Count == 0)
        {
            return string.Empty;
        }

        used = true;
        var joiner = string.Equals(logic, "or", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ";
        var text = string.Join(joiner, clauses);
        return clauses.Count > 1 ? "(" + text + ")" : text;
    }

    /// <summary>ORDER BY 句（グループ → 並べ替えの順）。</summary>
    public static string BuildOrderBy(ReportDefinition definition)
    {
        var parts = new List<string>();
        foreach (var grouping in definition.Groupings)
        {
            if (string.IsNullOrWhiteSpace(grouping.Field))
            {
                continue;
            }

            parts.Add(grouping.Field.Trim() + (string.Equals(grouping.Direction, "desc", StringComparison.OrdinalIgnoreCase) ? " DESC" : " ASC"));
        }

        foreach (var sort in definition.Sorts)
        {
            if (string.IsNullOrWhiteSpace(sort.Field))
            {
                continue;
            }

            parts.Add(sort.Field.Trim() + (sort.Descending ? " DESC" : " ASC"));
        }

        return string.Join(", ", parts);
    }

    /// <summary>フィルタ 1 件を SOQL 条件へ変換する。</summary>
    public static string BuildClause(ReportFilter filter)
    {
        var field = filter.Field.Trim();
        var op = filter.Operator;
        switch (op)
        {
            case ReportOperators.IsNull:
                return field + " = null";
            case ReportOperators.IsNotNull:
                return field + " != null";
            case ReportOperators.DateLiteral:
                return field + " = " + (filter.Value ?? "").Trim().ToUpperInvariant();
            case ReportOperators.In:
            case ReportOperators.NotIn:
            {
                var values = SplitValues(filter.Value).Select(FormatValue).ToList();
                if (values.Count == 0)
                {
                    return string.Empty;
                }

                return field + (op == ReportOperators.In ? " IN (" : " NOT IN (") + string.Join(", ", values) + ")";
            }

            case ReportOperators.Contains:
                return field + " LIKE " + Quote("%" + (filter.Value ?? "").Trim() + "%");
            case ReportOperators.StartsWith:
                return field + " LIKE " + Quote((filter.Value ?? "").Trim() + "%");
            case ReportOperators.EndsWith:
                return field + " LIKE " + Quote("%" + (filter.Value ?? "").Trim());
            case ReportOperators.NotEquals:
                return field + " != " + FormatValue(filter.Value);
            case ReportOperators.LessThan:
                return field + " < " + FormatValue(filter.Value);
            case ReportOperators.LessThanOrEqual:
                return field + " <= " + FormatValue(filter.Value);
            case ReportOperators.GreaterThan:
                return field + " > " + FormatValue(filter.Value);
            case ReportOperators.GreaterThanOrEqual:
                return field + " >= " + FormatValue(filter.Value);
            default:
                return field + " = " + FormatValue(filter.Value);
        }
    }

    /// <summary>値を SOQL リテラルへ変換する（数値 / 真偽値 / 日付はクォートしない）。</summary>
    public static string FormatValue(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
        {
            return "null";
        }

        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            return "TRUE";
        }

        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
        {
            return "FALSE";
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
        {
            return moment.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        return Quote(text);
    }

    /// <summary>SOQL 文字列リテラルにする（シングル クォートをエスケープ）。</summary>
    public static string Quote(string value) =>
        "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

    private static IEnumerable<string> SplitValues(string? value) =>
        (value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => v.Length > 0);
}
