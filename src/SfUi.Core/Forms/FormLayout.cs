namespace SfUi.Core;

/// <summary>帳票の用紙・ページ分割の計算（mm 単位）。</summary>
public static class FormLayout
{
    /// <summary>用紙サイズ（幅 / 高さ mm）。</summary>
    public static (double WidthMm, double HeightMm) PageSize(FormDefinition form) =>
        ReportPapers.SizeMm(form.PaperSize, form.Orientation);

    /// <summary>明細表の 1 ページあたりの行数（最低 1）。</summary>
    public static int RowsPerPage(FormDefinition form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.Table is not { } table)
        {
            return 0;
        }

        if (table.MaxRowsPerPage > 0)
        {
            return table.MaxRowsPerPage;
        }

        var (_, height) = PageSize(form);
        var rowsTop = table.Y + (table.ShowHeader ? table.RowHeightMm : 0);
        var footerTop = form.Footer.Count == 0
            ? height - form.MarginMm
            : form.Footer.Min(e => e.Y);
        var available = footerTop - 4 - rowsTop;
        return Math.Max(1, (int)Math.Floor(available / Math.Max(1, table.RowHeightMm)));
    }

    /// <summary>ページ数（明細が 0 件でも 1 ページ）。</summary>
    public static int PageCount(FormDefinition form, int rowCount)
    {
        var perPage = RowsPerPage(form);
        if (perPage <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Ceiling(Math.Max(0, rowCount) / (double)perPage));
    }

    /// <summary>明細行をページごとに分割する。</summary>
    public static IReadOnlyList<IReadOnlyList<IReadOnlyList<string?>>> SplitRows(FormDefinition form, ReportTable? table)
    {
        var rows = table?.Rows ?? Array.Empty<IReadOnlyList<string?>>();
        var perPage = RowsPerPage(form);
        var pages = PageCount(form, rows.Count);
        var result = new List<IReadOnlyList<IReadOnlyList<string?>>>(pages);
        for (var page = 0; page < pages; page++)
        {
            var skip = perPage <= 0 ? 0 : page * perPage;
            var take = perPage <= 0 ? 0 : perPage;
            result.Add(rows.Skip(skip).Take(take).ToList());
        }

        return result;
    }

    /// <summary>{Field} 形式の差し込みを置換する（未知のキーはそのまま）。</summary>
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

    /// <summary>カテゴリの CSS align。</summary>
    public static string AlignCss(string? align) => align?.ToLowerInvariant() switch
    {
        "center" => "center",
        "right" => "right",
        _ => "left",
    };
}
