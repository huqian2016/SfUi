using System.Text;

namespace SfUi.Core;

/// <summary>帳票の用紙サイズ。</summary>
public static class ReportPapers
{
    public const string A4 = "A4";
    public const string A3 = "A3";
    public const string Letter = "Letter";

    public static IReadOnlyList<string> All { get; } = new[] { A4, A3, Letter };

    /// <summary>用紙の幅 / 高さ（mm）。</summary>
    public static (double WidthMm, double HeightMm) SizeMm(string? paper, string? orientation)
    {
        var (width, height) = (paper ?? A4) switch
        {
            A3 => (297d, 420d),
            Letter => (215.9d, 279.4d),
            _ => (210d, 297d),
        };

        return IsLandscape(orientation) ? (height, width) : (width, height);
    }

    public static bool IsLandscape(string? orientation) =>
        string.Equals(orientation, "landscape", StringComparison.OrdinalIgnoreCase);
}

/// <summary>帳票レイアウトのページ計算（1 ページに入る明細行数）。</summary>
public static class ReportFormLayout
{
    /// <summary>ページ番号 / 差し込みフィールドの既定プレースホルダー。</summary>
    public const string PlaceholderTitle = "{Title}";

    public const string PlaceholderPage = "{Page}";

    public const string PlaceholderPages = "{Pages}";

    public const string PlaceholderDate = "{Date}";

    public const string PlaceholderOrg = "{Org}";

    public const string PlaceholderRows = "{Rows}";

    /// <summary>明細 1 行の高さ（mm・既定）。</summary>
    public const double DetailRowHeightMm = 7;

    /// <summary>1 ページに入る明細行数（最低 1 行）。</summary>
    public static int RowsPerPage(ReportLayoutDefinition layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var (_, pageHeight) = ReportPapers.SizeMm(layout.PaperSize, layout.Orientation);
        var usable = pageHeight - (Math.Max(0, layout.MarginMm) * 2);
        var header = layout.Header?.HeightMm ?? 0;
        var footer = layout.Footer?.HeightMm ?? 0;
        var summary = layout.Summary?.HeightMm ?? 0;
        var rowHeight = layout.Detail?.RowHeightMm is > 0 and var h ? h : DetailRowHeightMm;
        var available = usable - header - footer - summary - 8;
        return Math.Max(1, (int)Math.Floor(available / rowHeight));
    }

    /// <summary>ページ数（明細が 0 件でも 1 ページ）。</summary>
    public static int PageCount(ReportLayoutDefinition layout, int rowCount) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, rowCount) / (double)RowsPerPage(layout)));

    /// <summary>差し込みフィールドを置換する（未知のフィールドはそのまま残す）。</summary>
    public static string Substitute(string? text, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = text;
        foreach (var pair in values)
        {
            result = result.Replace("{" + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}

/// <summary>
/// 帳票（SVF 風）の HTML を組み立てる。
/// ヘッダー / 明細（レコード繰り返し）/ 集計 / フッターを 1 ページずつ描画し、
/// ブラウザーでそのまま印刷できる CSS を埋め込む。
/// </summary>
public static class ReportFormWriter
{
    /// <summary>帳票 HTML を返す。</summary>
    public static string Write(ReportDocument document, ReportLayoutDefinition layout)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);

        var table = document.Tables.FirstOrDefault();
        var columns = table?.Columns ?? Array.Empty<string>();
        var rows = table?.Rows ?? Array.Empty<IReadOnlyList<string?>>();
        var rowsPerPage = ReportFormLayout.RowsPerPage(layout);
        var pageCount = ReportFormLayout.PageCount(layout, rows.Count);
        var (widthMm, heightMm) = ReportPapers.SizeMm(layout.PaperSize, layout.Orientation);
        var margin = Math.Max(0, layout.MarginMm);

        var builder = new StringBuilder();
        builder.Append("<!DOCTYPE html>\n<html lang=\"").Append(UiText.Language).Append("\">\n<head>\n");
        builder.Append("<meta charset=\"utf-8\">\n");
        builder.Append("<title>").Append(Escape(document.Title)).Append("</title>\n");
        builder.Append("<style>\n");
        builder.Append("@page { size: ").Append(widthMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
            .Append("mm ").Append(heightMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
            .Append("mm; margin: ").Append(margin.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append("mm; }\n");
        builder.Append("body { margin: 0; background: #f2f2f2; ");
        builder.Append("font-family: ").Append(string.IsNullOrWhiteSpace(layout.FontFamily)
            ? "'Segoe UI', 'Hiragino Sans', 'Noto Sans JP', sans-serif"
            : Escape(layout.FontFamily!));
        builder.Append("; font-size: 10pt; color: #111; }\n");
        builder.Append(".page { position: relative; box-sizing: border-box; ");
        builder.Append("width: ").Append(widthMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append("mm; ");
        builder.Append("min-height: ").Append(heightMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append("mm; ");
        builder.Append("padding: ").Append(margin.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append("mm; ");
        builder.Append("margin: 0 auto 6mm; background: #fff; page-break-after: always; }\n");
        builder.Append(".page:last-child { page-break-after: auto; }\n");
        builder.Append(".region { overflow: hidden; }\n");
        builder.Append(".detail { width: 100%; border-collapse: collapse; }\n");
        builder.Append(".detail th { background: #f3f5f8; border-bottom: 1px solid #9aa4b2; text-align: left; padding: 1.2mm; font-weight: 600; }\n");
        builder.Append(".detail td { border-bottom: 1px solid #d6dbe3; padding: 1.2mm; }\n");
        builder.Append(".detail td.num { text-align: right; font-variant-numeric: tabular-nums; }\n");
        builder.Append(".summary { margin-top: 3mm; border-top: 2px solid #333; padding-top: 2mm; }\n");
        builder.Append(".foot { color: #555; }\n");
        builder.Append("@media print { body { background: #fff; } .page { margin: 0; box-shadow: none; } }\n");
        builder.Append("</style>\n</head>\n<body>\n");

        var numericColumns = table?.NumericColumns ?? new HashSet<int>();
        for (var page = 1; page <= pageCount; page++)
        {
            var pageRows = rows.Skip((page - 1) * rowsPerPage).Take(rowsPerPage).ToList();
            var common = CommonValues(document, table?.Name, page, pageCount, rows.Count);

            builder.Append("<div class=\"page\">\n");
            AppendRegion(builder, "header", layout.Header, common, Escape);

            builder.Append("<div class=\"region detail-wrap\">\n");
            if (!string.IsNullOrWhiteSpace(layout.Detail?.Text))
            {
                builder.Append("<p>").Append(Format(layout.Detail!.Text, common)).Append("</p>\n");
            }

            builder.Append("<table class=\"detail\">\n<thead><tr>");
            foreach (var column in columns)
            {
                builder.Append("<th>").Append(Escape(column)).Append("</th>");
            }

            builder.Append("</tr></thead>\n<tbody>\n");
            if (pageRows.Count == 0)
            {
                builder.Append("<tr><td colspan=\"").Append(Math.Max(1, columns.Count)).Append("\" class=\"foot\">")
                    .Append(Escape(UiText.T("Report_FormNoRows"))).Append("</td></tr>\n");
            }

            foreach (var row in pageRows)
            {
                builder.Append("<tr>");
                for (var i = 0; i < columns.Count; i++)
                {
                    var value = i < row.Count ? row[i] : null;
                    builder.Append(numericColumns.Contains(i) ? "<td class=\"num\">" : "<td>")
                        .Append(Escape(value ?? string.Empty)).Append("</td>");
                }

                builder.Append("</tr>\n");
            }

            builder.Append("</tbody>\n</table>\n</div>\n");

            AppendRegion(builder, "summary", layout.Summary, common, Escape);
            AppendRegion(builder, "footer", layout.Footer, common, Escape);
            builder.Append("</div>\n");
        }

        builder.Append("</body>\n</html>\n");
        return builder.ToString();
    }

    /// <summary>差し込み値を返す（{Title} / {Page} / {Pages} / {Date} / {Org} / {Rows}）。</summary>
    public static IReadOnlyDictionary<string, string> CommonValues(
        ReportDocument document,
        string? tableName,
        int page,
        int pageCount,
        int rowCount)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = document.Title,
            ["Subtitle"] = document.Subtitle ?? string.Empty,
            ["Org"] = document.Org ?? string.Empty,
            ["Source"] = document.Source ?? "SfUi",
            ["Date"] = document.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture),
            ["Page"] = page.ToString(System.Globalization.CultureInfo.CurrentCulture),
            ["Pages"] = pageCount.ToString(System.Globalization.CultureInfo.CurrentCulture),
            ["Rows"] = rowCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            ["Table"] = tableName ?? string.Empty,
        };

        return values;
    }

    private static void AppendRegion(
        StringBuilder builder,
        string cssClass,
        ReportLayoutRegion? region,
        IReadOnlyDictionary<string, string> values,
        Func<string, string> escape)
    {
        if (region is null || (string.IsNullOrWhiteSpace(region.Text) && region.Fields.Count == 0))
        {
            return;
        }

        builder.Append("<div class=\"region ").Append(cssClass).Append("\" style=\"")
            .Append("text-align: ").Append(AlignCss(region.Align)).Append("; ")
            .Append("min-height: ").Append(region.HeightMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append("mm;\">\n");

        if (!string.IsNullOrWhiteSpace(region.Text))
        {
            builder.Append("<div>").Append(Format(region.Text, values, escape)).Append("</div>\n");
        }

        if (region.Fields.Count > 0)
        {
            builder.Append("<div>");
            for (var i = 0; i < region.Fields.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" &#183; ");
                }

                var field = region.Fields[i];
                builder.Append(escape(values.TryGetValue(field, out var value) ? value : "{" + field + "}"));
            }

            builder.Append("</div>\n");
        }

        builder.Append("</div>\n");
    }

    private static string Format(string text, IReadOnlyDictionary<string, string> values, Func<string, string>? escape = null)
    {
        var substituted = ReportFormLayout.Substitute(text, values);
        var encoded = escape is null ? Escape(substituted) : escape(substituted);
        return encoded.Replace("\n", "<br>", StringComparison.Ordinal);
    }

    private static string AlignCss(string? align) => align?.ToLowerInvariant() switch
    {
        "center" => "center",
        "right" => "right",
        _ => "left",
    };

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
