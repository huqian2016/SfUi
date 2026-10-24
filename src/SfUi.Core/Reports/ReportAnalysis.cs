using System.Globalization;

namespace SfUi.Core;

/// <summary>
/// レポートの表から「分析」セクションを作る（行数・空欄・数値集計・上位値・重複）。
/// 出力ダイアログの「分析を付ける」がオンのときに <see cref="ReportExportService"/> が追加する。
/// </summary>
public static class ReportAnalysis
{
    /// <summary>上位値を出す件数。</summary>
    public const int TopValues = 5;

    /// <summary>1 つの表を分析してセクションを返す（データが無い場合も行数 0 を返す）。</summary>
    public static ReportSection Analyze(ReportTable table)
    {
        var bullets = new List<string>
        {
            UiText.T("Report_AnalysisRowsFmt", table.RowCount.ToString("N0"), table.Columns.Count),
        };

        var emptyCells = 0;
        foreach (var row in table.Rows)
        {
            for (var i = 0; i < table.Columns.Count; i++)
            {
                if (i >= row.Count || string.IsNullOrWhiteSpace(row[i]))
                {
                    emptyCells++;
                }
            }
        }

        if (emptyCells > 0)
        {
            bullets.Add(UiText.T("Report_AnalysisEmptyFmt", emptyCells.ToString("N0")));
        }

        for (var column = 0; column < table.Columns.Count; column++)
        {
            var header = table.Columns[column];
            var values = table.Rows
                .Select(row => column < row.Count ? row[column]?.Trim() : null)
                .Where(value => !string.IsNullOrEmpty(value))
                .ToList();
            if (values.Count == 0)
            {
                continue;
            }

            if (table.NumericColumns.Contains(column)
                && TryAggregate(values, out var sum, out var min, out var max, out var average, out var numericCount))
            {
                bullets.Add(UiText.T(
                    "Report_AnalysisNumericFmt",
                    header,
                    numericCount.ToString("N0"),
                    Format(sum),
                    Format(average),
                    Format(min),
                    Format(max)));
                continue;
            }

            var distinct = values.Distinct(StringComparer.Ordinal).Count();
            if (distinct <= 1)
            {
                bullets.Add(UiText.T("Report_AnalysisConstantFmt", header, values[0]));
                continue;
            }

            if (distinct <= Math.Max(TopValues, values.Count / 4))
            {
                var top = values
                    .GroupBy(value => value!, StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.Ordinal)
                    .Take(TopValues)
                    .Select(group => $"{group.Key} ({group.Count().ToString("N0")})");
                bullets.Add(UiText.T("Report_AnalysisTopFmt", header, distinct.ToString("N0"), string.Join(", ", top)));
            }
            else
            {
                bullets.Add(UiText.T("Report_AnalysisDistinctFmt", header, distinct.ToString("N0"), values.Count.ToString("N0")));
            }
        }

        var duplicates = table.Rows
            .GroupBy(row => string.Join('\u0001', row.Select(cell => cell ?? string.Empty)))
            .Count(group => group.Count() > 1);
        if (duplicates > 0)
        {
            bullets.Add(UiText.T("Report_AnalysisDuplicatesFmt", duplicates.ToString("N0")));
        }

        return ReportSection.FromBullets(UiText.T("Report_AnalysisHeadingFmt", table.Name), bullets);
    }

    private static bool TryAggregate(
        IReadOnlyList<string> values,
        out double sum,
        out double min,
        out double max,
        out double average,
        out int count)
    {
        sum = 0;
        min = double.MaxValue;
        max = double.MinValue;
        average = 0;
        count = 0;

        foreach (var value in values)
        {
            if (!double.TryParse(
                    value.Replace(",", string.Empty, StringComparison.Ordinal).Replace("%", string.Empty, StringComparison.Ordinal),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                continue;
            }

            count++;
            sum += parsed;
            min = Math.Min(min, parsed);
            max = Math.Max(max, parsed);
        }

        if (count == 0)
        {
            return false;
        }

        average = sum / count;
        return true;
    }

    private static string Format(double value) =>
        Math.Abs(value) >= 1000 || value == Math.Truncate(value)
            ? value.ToString("N0", CultureInfo.CurrentCulture)
            : value.ToString("N2", CultureInfo.CurrentCulture);
}
