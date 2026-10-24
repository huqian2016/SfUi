using System.Data;
using SfUi.Core;

namespace SfUi.Presentation;

/// <summary>各機能のデータから <see cref="ReportDocument"/> を組み立てる共通ヘルパー。</summary>
public static class ReportDocumentFactory
{
    /// <summary>DataTable から表を作る（数値型の列は右寄せ・集計対象になる）。</summary>
    public static ReportTable TableFromDataTable(string name, DataTable table)
    {
        var columns = table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToList();
        var numeric = new HashSet<int>();
        for (var i = 0; i < table.Columns.Count; i++)
        {
            if (IsNumeric(table.Columns[i].DataType))
            {
                numeric.Add(i);
            }
        }

        var rows = new List<IReadOnlyList<string?>>();
        foreach (DataRow row in table.Rows)
        {
            var values = new string?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                var value = row[i];
                values[i] = value is null or DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.CurrentCulture);
            }

            rows.Add(values);
        }

        return new ReportTable { Name = name, Columns = columns, Rows = rows, NumericColumns = numeric };
    }

    /// <summary>表 1 つだけのレポート。</summary>
    public static ReportDocument FromTable(string title, string? org, string? subtitle, ReportTable table)
    {
        var document = NewDocument(title, org, subtitle);
        document.Sections.Add(ReportSection.FromTable(table));
        return document;
    }

    /// <summary>表 + 追加セクション（クエリ文字列など）を持つレポート。</summary>
    public static ReportDocument FromTable(
        string title,
        string? org,
        string? subtitle,
        ReportTable table,
        params ReportSection[] extraSections)
    {
        var document = NewDocument(title, org, subtitle);
        document.Sections.AddRange(extraSections);
        document.Sections.Add(ReportSection.FromTable(table));
        return document;
    }

    /// <summary>テキストのみのレポート（ログ・出力など）。</summary>
    public static ReportDocument FromText(string title, string? org, string? subtitle, params (string Heading, string Text)[] sections)
    {
        var document = NewDocument(title, org, subtitle);
        foreach (var (heading, text) in sections)
        {
            document.Sections.Add(new ReportSection { Heading = heading, Text = text });
        }

        return document;
    }

    private static ReportDocument NewDocument(string title, string? org, string? subtitle) =>
        new()
        {
            Title = title,
            Org = org,
            Subtitle = subtitle,
            Source = "SfUi " + typeof(ReportDocument).Assembly.GetName().Version?.ToString(3),
        };

    private static bool IsNumeric(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(byte) || underlying == typeof(sbyte) || underlying == typeof(short)
            || underlying == typeof(ushort) || underlying == typeof(int) || underlying == typeof(uint)
            || underlying == typeof(long) || underlying == typeof(ulong) || underlying == typeof(float)
            || underlying == typeof(double) || underlying == typeof(decimal);
    }
}
