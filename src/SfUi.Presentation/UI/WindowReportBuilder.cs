using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// ウィンドウ単位のレポート出力で使う小さなヘルパー。
/// 列名と行をそのまま ReportTable / ReportDocument へ変換する。
/// </summary>
public static class WindowReportBuilder
{
    public static ReportTable Table(
        string name,
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string?>> rows,
        params int[] numericColumns)
    {
        var materialized = new List<IReadOnlyList<string?>>();
        foreach (var row in rows)
        {
            var values = new List<string?>(columns.Count);
            for (var i = 0; i < columns.Count; i++)
            {
                values.Add(i < row.Count ? row[i] : null);
            }

            materialized.Add(values);
        }

        return new ReportTable
        {
            Name = name,
            Columns = columns,
            Rows = materialized,
            NumericColumns = new HashSet<int>(numericColumns),
        };
    }

    public static ReportDocument Document(
        string title,
        string? subtitle,
        string? org,
        IEnumerable<ReportSection> sections,
        string? source = null)
    {
        var document = new ReportDocument
        {
            Title = title,
            Subtitle = subtitle,
            Org = org,
            Source = source ?? DefaultSource,
            GeneratedAt = DateTimeOffset.Now,
        };

        foreach (var section in sections)
        {
            document.Sections.Add(section);
        }

        return document;
    }

    public static ReportSection? Section(string heading, ReportTable? table)
    {
        if (table is null || table.Rows.Count == 0)
        {
            return null;
        }

        return new ReportSection { Heading = heading, Table = table };
    }

    public static string DefaultSource =>
        "SfUi " + (typeof(WindowReportBuilder).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
}