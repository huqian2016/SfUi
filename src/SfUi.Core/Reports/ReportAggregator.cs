using System.Data;
using System.Globalization;

namespace SfUi.Core;

/// <summary>
/// 実行結果（<see cref="DataTable"/>）を画面・出力用の <see cref="LocalReportResult"/> へ整形する。
/// グループ化と集計はクライアント側で行う（Salesforce レポートの「グループ + 小計 + 合計」表示に合わせる）。
/// </summary>
public static class ReportAggregator
{
    /// <summary>データ テーブルをレポート結果へ変換する。</summary>
    public static LocalReportResult Aggregate(
        ReportDefinition definition,
        DataTable table,
        string engine,
        TimeSpan duration,
        string soql,
        string? org)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(table);

        var limit = definition.RowLimit;
        var truncated = limit > 0 && table.Rows.Count > limit;

        var rows = new List<DataRow>();
        foreach (DataRow row in table.Rows)
        {
            if (limit > 0 && rows.Count >= limit)
            {
                break;
            }

            rows.Add(row);
        }

        var columns = definition.VisibleColumns;
        var headers = columns.Select(ColumnLabel).ToList();
        var numeric = new HashSet<int>();
        var body = new List<IReadOnlyList<string?>>(rows.Count);

        foreach (var row in rows)
        {
            var values = new List<string?>(columns.Count);
            for (var i = 0; i < columns.Count; i++)
            {
                var value = ReadValue(row, columns[i].Field);
                if (ReportValueFormatter.ToDecimal(value) is not null)
                {
                    numeric.Add(i);
                }

                values.Add(ReportValueFormatter.Format(value, columns[i].Format));
            }

            body.Add(values);
        }

        var reportTable = new ReportTable
        {
            Name = definition.DisplayName,
            Columns = headers,
            Rows = body,
            NumericColumns = numeric,
        };

        var aggregates = definition.Aggregates.Where(a => !string.IsNullOrWhiteSpace(a.Field)).ToList();
        var totals = new ReportGroup
        {
            Key = "*",
            Label = UiText.T("Report_TotalLabel"),
            RowCount = rows.Count,
            Metrics = BuildMetrics(aggregates, rows),
        };

        return new LocalReportResult
        {
            ReportId = definition.Id,
            ReportName = definition.DisplayName,
            ObjectName = definition.ObjectName,
            Org = org,
            Soql = soql,
            Table = reportTable,
            Groups = BuildGroups(definition, rows, aggregates),
            Totals = totals,
            Truncated = truncated,
            Engine = engine,
            Duration = duration,
        };
    }

    /// <summary>データ テーブルから項目値を取り出す（存在しなければ null）。</summary>
    public static object? ReadValue(DataRow row, string? field)
    {
        var name = (field ?? "").Trim();
        if (name.Length == 0)
        {
            return null;
        }

        if (row.Table.Columns.Contains(name))
        {
            return row[name];
        }

        // リレーション項目（Account.Name）が末尾名だけで格納されている場合のフォールバック
        var last = name.Split('.')[^1];
        return row.Table.Columns.Contains(last) ? row[last] : null;
    }

    /// <summary>列ラベル（未設定なら項目 API 名の末尾）。</summary>
    public static string ColumnLabel(ReportColumn column) =>
        !string.IsNullOrWhiteSpace(column.Label) ? column.Label! : (column.Field.Split('.')[^1]);

    private static IReadOnlyList<ReportGroup> BuildGroups(
        ReportDefinition definition,
        IReadOnlyList<DataRow> rows,
        IReadOnlyList<ReportAggregate> aggregates)
    {
        var groupings = definition.Groupings.Where(g => !string.IsNullOrWhiteSpace(g.Field)).Take(3).ToList();
        if (groupings.Count == 0 || rows.Count == 0)
        {
            return Array.Empty<ReportGroup>();
        }

        var order = new List<string>();
        var buckets = new Dictionary<string, List<DataRow>>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var parts = new List<string>(groupings.Count);
            foreach (var grouping in groupings)
            {
                var raw = ReportValueFormatter.Format(ReadValue(row, grouping.Field), ReportValueFormats.Auto);
                parts.Add(ReportValueFormatter.ApplyDateGranularity(raw, grouping.DateGranularity));
            }

            var key = string.Join(" / ", parts);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new List<DataRow>();
                buckets[key] = bucket;
                order.Add(key);
            }

            bucket.Add(row);
        }

        var blank = UiText.T("Report_BlankGroup");
        var result = new List<ReportGroup>(order.Count);
        foreach (var key in order)
        {
            var bucket = buckets[key];
            result.Add(new ReportGroup
            {
                Key = key,
                Label = string.IsNullOrWhiteSpace(key) ? blank : key,
                RowCount = bucket.Count,
                Metrics = BuildMetrics(aggregates, bucket),
            });
        }

        return result;
    }

    private static IReadOnlyList<ReportMetric> BuildMetrics(IReadOnlyList<ReportAggregate> aggregates, IReadOnlyList<DataRow> rows)
    {
        if (aggregates.Count == 0)
        {
            return Array.Empty<ReportMetric>();
        }

        var metrics = new List<ReportMetric>(aggregates.Count);
        foreach (var aggregate in aggregates)
        {
            var label = !string.IsNullOrWhiteSpace(aggregate.Label)
                ? aggregate.Label!
                : $"{ReportAggregateFunctions.ToLabel(aggregate.Function)}({aggregate.Field.Split('.')[^1]})";

            decimal value;
            string text;
            if (string.Equals(aggregate.Function, ReportAggregateFunctions.CountDistinct, StringComparison.OrdinalIgnoreCase))
            {
                var distinct = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in rows)
                {
                    var raw = ReportValueFormatter.Format(ReadValue(row, aggregate.Field), ReportValueFormats.Auto);
                    if (raw.Length > 0)
                    {
                        distinct.Add(raw);
                    }
                }

                value = distinct.Count;
                text = value.ToString("N0", CultureInfo.CurrentCulture);
            }
            else if (string.Equals(aggregate.Function, ReportAggregateFunctions.Count, StringComparison.OrdinalIgnoreCase))
            {
                value = rows.Count;
                text = value.ToString("N0", CultureInfo.CurrentCulture);
            }
            else
            {
                var numbers = new List<decimal>(rows.Count);
                foreach (var row in rows)
                {
                    if (ReportValueFormatter.ToDecimal(ReadValue(row, aggregate.Field)) is { } number)
                    {
                        numbers.Add(number);
                    }
                }

                value = ReportAggregateFunctions.Apply(aggregate.Function, numbers);
                text = value.ToString("#,##0.####", CultureInfo.CurrentCulture);
            }

            metrics.Add(new ReportMetric(label, aggregate.Function, text, value));
        }

        return metrics;
    }
}
