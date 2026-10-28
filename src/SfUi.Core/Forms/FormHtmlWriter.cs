using System.Globalization;
using System.Text;

namespace SfUi.Core;

/// <summary>
/// 帳票定義を、用紙サイズそのままのページに分割した HTML へ変換する。
/// 帳票要素は mm 指定の絶対配置で描き、ブラウザーの印刷（@page）でも同じ見た目になる。
/// </summary>
public static class FormHtmlWriter
{
    /// <summary>帳票 HTML を返す（1 ファイルに全ページ）。</summary>
    public static string Write(FormDefinition form, FormData data)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(data);

        var (width, height) = FormLayout.PageSize(form);
        var pages = FormLayout.SplitRows(form, data.Table);
        var pageCount = pages.Count;
        var builder = new StringBuilder();

        builder.Append("<!DOCTYPE html>\n<html lang=\"").Append(UiText.Language).Append("\">\n<head>\n");
        builder.Append("<meta charset=\"utf-8\">\n<title>").Append(Escape(data.Title)).Append("</title>\n");
        builder.Append("<style>\n");
        builder.Append("@page { size: ").Append(Mm(width)).Append("mm ").Append(Mm(height)).Append("mm; margin: 0; }\n");
        builder.Append("* { box-sizing: border-box; }\n");
        builder.Append("body { margin: 0; background: #f2f2f2; font-family: 'Yu Gothic UI','Meiryo','Hiragino Sans','Noto Sans JP',sans-serif; color: #111; }\n");
        builder.Append(".page { position: relative; width: ").Append(Mm(width)).Append("mm; height: ").Append(Mm(height))
            .Append("mm; background: #fff; margin: 0 auto 6mm; overflow: hidden; page-break-after: always; }\n");
        builder.Append(".page:last-child { page-break-after: auto; }\n");
        builder.Append(".el { position: absolute; white-space: pre-wrap; overflow: hidden; line-height: 1.3; }\n");
        builder.Append(".rule { position: absolute; background: #333; }\n");
        builder.Append(".box { position: absolute; border: 1px solid #333; }\n");
        builder.Append(".rows { position: absolute; }\n");
        builder.Append(".row { display: flex; align-items: center; }\n");
        builder.Append(".cell { overflow: hidden; padding: 0 1.2mm; white-space: nowrap; text-overflow: ellipsis; }\n");
        builder.Append(".head { background: #F3F5F8; font-weight: 600; border-bottom: 1px solid #9AA4B2; }\n");
        builder.Append(".line { border-bottom: 1px solid #D6DBE3; }\n");
        builder.Append("@media print { body { background: #fff; } .page { margin: 0; box-shadow: none; } }\n");
        builder.Append("</style>\n</head>\n<body>\n");

        for (var page = 1; page <= pageCount; page++)
        {
            var values = data.BuildValues(page, pageCount);
            builder.Append("<div class=\"page\">\n");
            AppendElements(builder, form.Header, values, form.FontSize);
            AppendTable(builder, form, data, pages[page - 1], page, pageCount, values);
            AppendElements(builder, form.Footer, values, form.FontSize);
            builder.Append("</div>\n");
        }

        builder.Append("</body>\n</html>\n");
        return builder.ToString();
    }

    private static void AppendElements(
        StringBuilder builder,
        IReadOnlyList<FormElement> elements,
        IReadOnlyDictionary<string, string> values,
        double defaultFontSize)
    {
        foreach (var element in elements)
        {
            var type = element.Type ?? FormElementTypes.Text;
            if (string.Equals(type, FormElementTypes.Line, StringComparison.OrdinalIgnoreCase))
            {
                var lineWidth = element.Vertical ? Math.Max(0.2, element.Width) : Math.Max(0.2, element.Width);
                var lineHeight = element.Vertical ? Math.Max(0.2, element.Height) : Math.Max(0.2, element.Height);
                builder.Append("<div class=\"rule\" style=\"")
                    .Append(Pos(element.X, element.Y))
                    .Append("width: ").Append(Mm(lineWidth)).Append("mm; height: ").Append(Mm(lineHeight)).Append("mm;\"></div>\n");
                continue;
            }

            if (string.Equals(type, FormElementTypes.Box, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append("<div class=\"box\" style=\"")
                    .Append(Pos(element.X, element.Y))
                    .Append("width: ").Append(Mm(Math.Max(1, element.Width))).Append("mm; height: ")
                    .Append(Mm(Math.Max(1, element.Height))).Append("mm;\"></div>\n");
                continue;
            }

            var text = FormLayout.Substitute(element.Text, values);
            builder.Append("<div class=\"el\" style=\"")
                .Append(Pos(element.X, element.Y))
                .Append("width: ").Append(Mm(Math.Max(4, element.Width))).Append("mm; ")
                .Append("min-height: ").Append(Mm(Math.Max(3, element.Height))).Append("mm; ")
                .Append("font-size: ").Append((element.FontSize > 0 ? element.FontSize : defaultFontSize).ToString("0.##", CultureInfo.InvariantCulture))
                .Append("pt; ")
                .Append("text-align: ").Append(FormLayout.AlignCss(element.Align)).Append("; ");

            if (element.Bold)
            {
                builder.Append("font-weight: 700; ");
            }

            if (!string.IsNullOrWhiteSpace(element.Border))
            {
                builder.Append(element.Border!.ToLowerInvariant() switch
                {
                    "box" => "border: 1px solid #333; ",
                    "underline" => "border-bottom: 1px solid #333; ",
                    _ => "",
                });
            }

            builder.Append("\">").Append(Escape(text)).Append("</div>\n");
        }
    }

    private static void AppendTable(
        StringBuilder builder,
        FormDefinition form,
        FormData data,
        IReadOnlyList<IReadOnlyList<string?>> rows,
        int page,
        int pageCount,
        IReadOnlyDictionary<string, string> values)
    {
        if (form.Table is not { } table)
        {
            return;
        }

        var columns = table.Columns.Count > 0
            ? table.Columns
            : BuildColumnsFromTable(data.Table);

        builder.Append("<div class=\"rows\" style=\"").Append(Pos(table.X, table.Y))
            .Append("width: ").Append(Mm(table.Width)).Append("mm;\">\n");

        if (table.ShowHeader)
        {
            builder.Append("<div class=\"row head\" style=\"height: ").Append(Mm(table.RowHeightMm)).Append("mm;\">");
            foreach (var column in columns)
            {
                builder.Append("<div class=\"cell\" style=\"width: ").Append(Mm(column.WidthMm)).Append("mm; ")
                    .Append("text-align: ").Append(FormLayout.AlignCss(column.Align)).Append(";\">")
                    .Append(Escape(FormLayout.Substitute(column.Label, values))).Append("</div>");
            }

            builder.Append("</div>\n");
        }

        var columnIndexes = ResolveColumnIndexes(columns, data.Table);
        if (rows.Count == 0)
        {
            builder.Append("<div class=\"row line\" style=\"height: ").Append(Mm(table.RowHeightMm)).Append("mm;\">")
                .Append("<div class=\"cell\" style=\"width: ").Append(Mm(table.Width)).Append("mm;\">")
                .Append(Escape(UiText.T("Report_FormNoRows"))).Append("</div></div>\n");
        }

        foreach (var row in rows)
        {
            builder.Append("<div class=\"row line\" style=\"height: ").Append(Mm(table.RowHeightMm)).Append("mm;\">");
            for (var c = 0; c < columns.Count; c++)
            {
                var index = columnIndexes[c];
                var text = index >= 0 && index < row.Count ? row[index] ?? "" : "";
                var align = string.IsNullOrWhiteSpace(columns[c].Align) ? DefaultAlign(data.Table, index) : columns[c].Align;
                builder.Append("<div class=\"cell\" style=\"width: ").Append(Mm(columns[c].WidthMm)).Append("mm; ")
                    .Append("text-align: ").Append(FormLayout.AlignCss(align)).Append(";\">")
                    .Append(Escape(text)).Append("</div>");
            }

            builder.Append("</div>\n");
        }

        builder.Append("</div>\n");
        _ = page;
        _ = pageCount;
    }

    /// <summary>列定義が無い場合はレポートの列から作る（幅は均等）。</summary>
    internal static List<FormTableColumn> BuildColumnsFromTable(ReportTable? table)
    {
        if (table is null || table.Columns.Count == 0)
        {
            return new List<FormTableColumn>();
        }

        var width = 186d / table.Columns.Count;
        return table.Columns
            .Select((column, i) => new FormTableColumn
            {
                Label = column,
                Field = column,
                WidthMm = width,
                Align = table.NumericColumns.Contains(i) ? "right" : "left",
            })
            .ToList();
    }

    /// <summary>列の <see cref="FormTableColumn.Field"/> をレポート表のインデックスへ解決する。</summary>
    internal static List<int> ResolveColumnIndexes(IReadOnlyList<FormTableColumn> columns, ReportTable? table)
    {
        var indexes = new List<int>(columns.Count);
        foreach (var column in columns)
        {
            var name = string.IsNullOrWhiteSpace(column.Field) ? column.Label : column.Field;
            var index = -1;
            if (table is not null && !string.IsNullOrWhiteSpace(name))
            {
                for (var i = 0; i < table.Columns.Count; i++)
                {
                    if (string.Equals(table.Columns[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }

            indexes.Add(index);
        }

        return indexes;
    }

    internal static string DefaultAlign(ReportTable? table, int index) =>
        table is not null && index >= 0 && table.NumericColumns.Contains(index) ? "right" : "left";

    private static string Pos(double x, double y) =>
        "left: " + Mm(x) + "mm; top: " + Mm(y) + "mm; ";

    internal static string Mm(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    internal static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
