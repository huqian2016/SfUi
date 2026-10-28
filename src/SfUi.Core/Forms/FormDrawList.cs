namespace SfUi.Core;

/// <summary>描画命令（SVG / 画像レンダラ共通。座標は mm）。</summary>
public abstract record FormDrawItem;

/// <summary>文字。</summary>
public sealed record FormTextItem(
    double X,
    double Y,
    double Width,
    double Height,
    string Text,
    double FontSizePt,
    bool Bold,
    string Align) : FormDrawItem;

/// <summary>矩形（枠 / 塗り）。</summary>
public sealed record FormBoxItem(
    double X,
    double Y,
    double Width,
    double Height,
    string? Fill,
    string? Stroke) : FormDrawItem;

/// <summary>線。</summary>
public sealed record FormLineItem(
    double X1,
    double Y1,
    double X2,
    double Y2) : FormDrawItem;

/// <summary>1 ページ分の描画命令。</summary>
public sealed record FormDrawPage(double WidthMm, double HeightMm, IReadOnlyList<FormDrawItem> Items);

/// <summary>帳票定義 + データから描画命令を作る（SVG / PNG / JPEG レンダラが使う）。</summary>
public static class FormDrawList
{
    private const string TextColor = "#111111";
    private const string LineColor = "#333333";
    private const string RowLineColor = "#C9D1DB";

    public static IReadOnlyList<FormDrawPage> Build(FormDefinition form, FormData data)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(data);

        var (width, height) = FormLayout.PageSize(form);
        var pages = FormLayout.SplitRows(form, data.Table);
        var result = new List<FormDrawPage>(pages.Count);

        for (var page = 1; page <= pages.Count; page++)
        {
            var values = data.BuildValues(page, pages.Count);
            var items = new List<FormDrawItem>();
            AppendElements(items, form.Header, values, form.FontSize);
            AppendTable(items, form, data, pages[page - 1], values);
            AppendElements(items, form.Footer, values, form.FontSize);
            result.Add(new FormDrawPage(width, height, items));
        }

        return result;
    }

    private static void AppendElements(
        List<FormDrawItem> items,
        IReadOnlyList<FormElement> elements,
        IReadOnlyDictionary<string, string> values,
        double defaultFontSize)
    {
        foreach (var element in elements)
        {
            var type = element.Type ?? FormElementTypes.Text;
            if (string.Equals(type, FormElementTypes.Line, StringComparison.OrdinalIgnoreCase))
            {
                if (element.Vertical)
                {
                    items.Add(new FormLineItem(
                        element.X + (element.Width / 2),
                        element.Y,
                        element.X + (element.Width / 2),
                        element.Y + element.Height));
                }
                else
                {
                    items.Add(new FormLineItem(element.X, element.Y, element.X + element.Width, element.Y));
                }

                continue;
            }

            if (string.Equals(type, FormElementTypes.Box, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new FormBoxItem(element.X, element.Y, element.Width, element.Height, null, LineColor));
                continue;
            }

            items.Add(new FormTextItem(
                element.X,
                element.Y,
                element.Width,
                element.Height,
                FormLayout.Substitute(element.Text, values),
                element.FontSize > 0 ? element.FontSize : defaultFontSize,
                element.Bold,
                element.Align));
        }
    }

    private static void AppendTable(
        List<FormDrawItem> items,
        FormDefinition form,
        FormData data,
        IReadOnlyList<IReadOnlyList<string?>> rows,
        IReadOnlyDictionary<string, string> values)
    {
        if (form.Table is not { } table)
        {
            return;
        }

        var columns = table.Columns.Count > 0 ? table.Columns : FormHtmlWriter.BuildColumnsFromTable(data.Table);
        if (columns.Count == 0)
        {
            return;
        }

        var y = table.Y;
        if (table.ShowHeader)
        {
            if (!string.IsNullOrWhiteSpace(table.HeaderBackground))
            {
                items.Add(new FormBoxItem(table.X, y, table.Width, table.RowHeightMm, table.HeaderBackground, null));
            }

            var x = table.X;
            foreach (var column in columns)
            {
                items.Add(new FormTextItem(
                    x + 1.2,
                    y + ((table.RowHeightMm - FormMetrics.PtToMm(form.FontSize)) / 2),
                    Math.Max(4, column.WidthMm - 2.4),
                    table.RowHeightMm,
                    FormLayout.Substitute(column.Label, values),
                    form.FontSize,
                    true,
                    column.Align));
                x += column.WidthMm;
            }

            y += table.RowHeightMm;
            items.Add(new FormLineItem(table.X, y, table.X + table.Width, y));
        }

        var indexes = FormHtmlWriter.ResolveColumnIndexes(columns, data.Table);
        if (rows.Count == 0)
        {
            items.Add(new FormTextItem(table.X + 1.2, y + 1.5, Math.Max(4, table.Width - 2.4), table.RowHeightMm,
                UiText.T("Report_FormNoRows"), form.FontSize, false, "left"));
            return;
        }

        foreach (var row in rows)
        {
            var x = table.X;
            for (var c = 0; c < columns.Count; c++)
            {
                var index = indexes[c];
                var text = index >= 0 && index < row.Count ? row[index] ?? "" : "";
                var align = string.IsNullOrWhiteSpace(columns[c].Align)
                    ? FormHtmlWriter.DefaultAlign(data.Table, index)
                    : columns[c].Align;
                items.Add(new FormTextItem(
                    x + 1.2,
                    y + ((table.RowHeightMm - FormMetrics.PtToMm(form.FontSize)) / 2),
                    Math.Max(4, columns[c].WidthMm - 2.4),
                    table.RowHeightMm,
                    text,
                    form.FontSize,
                    false,
                    align));
                x += columns[c].WidthMm;
            }

            y += table.RowHeightMm;
            items.Add(new FormLineItem(table.X, y, table.X + table.Width, y));
        }
    }

    internal static string TextColorValue => TextColor;

    internal static string RowLineColorValue => RowLineColor;
}
