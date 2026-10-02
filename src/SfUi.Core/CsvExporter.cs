using System.Data;
using System.Text;

namespace SfUi.Core;

/// <summary>DataTable を CSV（RFC 4180 準拠のエスケープ）へ変換する。</summary>
public static class CsvExporter
{
    public static string ToCsv(DataTable table, bool includeHeader = true)
    {
        var builder = new StringBuilder();
        var columns = table.Columns.Cast<DataColumn>().ToList();

        if (includeHeader)
        {
            builder.AppendLine(string.Join(",", columns.Select(c => Escape(c.ColumnName))));
        }

        foreach (DataRow row in table.Rows)
        {
            builder.AppendLine(string.Join(",", columns.Select(c => Escape(row[c] is DBNull ? null : row[c]?.ToString()))));
        }

        return builder.ToString();
    }

    /// <summary>CSV の 1 フィールドをエスケープする（カンマ・引用符・改行を含む場合は引用符で囲む）。</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
