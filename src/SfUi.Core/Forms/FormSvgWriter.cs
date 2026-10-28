using System.Globalization;
using System.Text;

namespace SfUi.Core;

/// <summary>帳票を SVG（ベクター）へ変換する。1 ページ 1 ファイル。</summary>
public static class FormSvgWriter
{
    /// <summary>ページごとの SVG 文字列を返す。</summary>
    public static IReadOnlyList<string> WritePages(FormDefinition form, FormData data)
    {
        var pages = FormDrawList.Build(form, data);
        return pages.Select(WritePage).ToList();
    }

    /// <summary>1 ページ分の SVG を返す。</summary>
    public static string WritePage(FormDrawPage page)
    {
        var builder = new StringBuilder();
        var width = Mm(page.WidthMm);
        var height = Mm(page.HeightMm);

        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(width).Append("mm\" height=\"")
            .Append(height).Append("mm\" viewBox=\"0 0 ").Append(width).Append(' ').Append(height).Append("\">\n");
        builder.Append("<rect x=\"0\" y=\"0\" width=\"").Append(width).Append("\" height=\"").Append(height)
            .Append("\" fill=\"#FFFFFF\"/>\n");
        builder.Append("<style>text { font-family: 'Yu Gothic UI','Meiryo','Hiragino Sans','Noto Sans JP',sans-serif; fill: ")
            .Append(FormDrawList.TextColorValue).Append("; }</style>\n");

        foreach (var item in page.Items)
        {
            switch (item)
            {
                case FormBoxItem box:
                    builder.Append("<rect x=\"").Append(Mm(box.X)).Append("\" y=\"").Append(Mm(box.Y))
                        .Append("\" width=\"").Append(Mm(Math.Max(0.1, box.Width))).Append("\" height=\"")
                        .Append(Mm(Math.Max(0.1, box.Height))).Append("\" fill=\"")
                        .Append(string.IsNullOrWhiteSpace(box.Fill) ? "none" : Escape(box.Fill!))
                        .Append("\" stroke=\"").Append(string.IsNullOrWhiteSpace(box.Stroke) ? "none" : Escape(box.Stroke!))
                        .Append("\" stroke-width=\"0.25\"/>\n");
                    break;

                case FormLineItem line:
                    builder.Append("<line x1=\"").Append(Mm(line.X1)).Append("\" y1=\"").Append(Mm(line.Y1))
                        .Append("\" x2=\"").Append(Mm(line.X2)).Append("\" y2=\"").Append(Mm(line.Y2))
                        .Append("\" stroke=\"").Append(FormDrawList.RowLineColorValue)
                        .Append("\" stroke-width=\"0.2\"/>\n");
                    break;

                case FormTextItem text:
                    AppendText(builder, text);
                    break;
            }
        }

        builder.Append("</svg>\n");
        return builder.ToString();
    }

    private static void AppendText(StringBuilder builder, FormTextItem item)
    {
        if (string.IsNullOrEmpty(item.Text))
        {
            return;
        }

        var anchor = item.Align?.ToLowerInvariant() switch
        {
            "center" => "middle",
            "right" => "end",
            _ => "start",
        };

        var x = item.Align?.ToLowerInvariant() switch
        {
            "center" => item.X + (item.Width / 2),
            "right" => item.X + item.Width,
            _ => item.X,
        };

        var baseline = item.Y + (FormMetrics.PtToMm(item.FontSizePt) * 0.95);
        var lines = item.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        builder.Append("<text x=\"").Append(Mm(x)).Append("\" y=\"").Append(Mm(baseline))
            .Append("\" font-size=\"").Append(Mm(FormMetrics.PtToMm(item.FontSizePt))).Append("\" text-anchor=\"").Append(anchor).Append('"');

        if (item.Bold)
        {
            builder.Append(" font-weight=\"700\"");
        }

        builder.Append('>');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.Append("<tspan x=\"").Append(Mm(x)).Append("\" dy=\"")
                    .Append(Mm(FormMetrics.PtToMm(item.FontSizePt) * 1.25)).Append("\">").Append(Escape(lines[i])).Append("</tspan>");
            }
            else
            {
                builder.Append("<tspan>").Append(Escape(lines[i])).Append("</tspan>");
            }
        }

        builder.Append("</text>\n");
    }

    private static string Mm(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
