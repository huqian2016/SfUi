using System.Data;
using System.Text;

namespace SfUi.Core;

/// <summary>DataTable を CSV（RFC 4180 準拠のエスケープ）へ変換する。</summary>
public static class CsvExporter
{
    public static string ToCsv(DataTable table, bool includeHeader = true)
    {
        return ToDelimited(table, ",", includeHeader);
    }

    /// <summary>DataTable を TSV（タブ区切り）へ変換する。</summary>
    public static string ToTsv(DataTable table, bool includeHeader = true)
    {
        return ToDelimited(table, "\t", includeHeader);
    }

    private static string ToDelimited(DataTable table, string separator, bool includeHeader)
    {
        var builder = new StringBuilder();
        var columns = table.Columns.Cast<DataColumn>().ToList();

        if (includeHeader)
        {
            builder.AppendLine(string.Join(separator, columns.Select(c => EscapeFor(c.ColumnName, separator))));
        }

        foreach (DataRow row in table.Rows)
        {
            builder.AppendLine(string.Join(separator, columns.Select(c => EscapeFor(row[c] is DBNull ? null : row[c]?.ToString(), separator))));
        }

        return builder.ToString();
    }

    /// <summary>CSV の 1 フィールドをエスケープする（カンマ・引用符・改行を含む場合は引用符で囲む）。</summary>
    public static string Escape(string? value)
    {
        return EscapeFor(value, ",");
    }

    private static string EscapeFor(string? value, string separator)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Contains(separator) || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
