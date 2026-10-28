namespace SfUi.Core;

/// <summary>帳票要素の種類。</summary>
public static class FormElementTypes
{
    /// <summary>固定文字（差し込み可）。</summary>
    public const string Text = "text";

    /// <summary>罫線（水平 / 垂直）。</summary>
    public const string Line = "line";

    /// <summary>矩形（枠）。</summary>
    public const string Box = "box";

    /// <summary>ページ番号（{Page} / {Pages} を自動差し込み）。</summary>
    public const string PageNumber = "page";

    public static IReadOnlyList<string> All { get; } = new[] { Text, Line, Box, PageNumber };

    public static string ToLabel(string? type) => (type ?? Text) switch
    {
        Line => UiText.T("Form_ElementLine"),
        Box => UiText.T("Form_ElementBox"),
        PageNumber => UiText.T("Form_ElementPage"),
        _ => UiText.T("Form_ElementText"),
    };

    public static ReportChoice ToChoice(string type) => new(type, ToLabel(type));
}

/// <summary>帳票の分類（テンプレートの絞り込みに使う）。</summary>
public static class FormCategories
{
    public const string Estimate = "estimate";
    public const string Order = "order";
    public const string Delivery = "delivery";
    public const string Invoice = "invoice";
    public const string Receipt = "receipt";
    public const string Payroll = "payroll";
    public const string Tax = "tax";
    public const string Statement = "statement";
    public const string General = "general";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        Estimate, Order, Delivery, Invoice, Receipt, Payroll, Tax, Statement, General,
    };

    public static string ToLabel(string? category) => (category ?? General) switch
    {
        Estimate => UiText.T("Form_CategoryEstimate"),
        Order => UiText.T("Form_CategoryOrder"),
        Delivery => UiText.T("Form_CategoryDelivery"),
        Invoice => UiText.T("Form_CategoryInvoice"),
        Receipt => UiText.T("Form_CategoryReceipt"),
        Payroll => UiText.T("Form_CategoryPayroll"),
        Tax => UiText.T("Form_CategoryTax"),
        Statement => UiText.T("Form_CategoryStatement"),
        _ => UiText.T("Form_CategoryGeneral"),
    };
}

/// <summary>帳票に置く 1 要素（位置は mm・用紙の左上原点）。</summary>
public sealed class FormElement
{
    public string Type { get; set; } = FormElementTypes.Text;

    /// <summary>固定文字。差し込みフィールド（{Title} / {Date} / {Total:金額} など）を含められる。</summary>
    public string? Text { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 60;

    public double Height { get; set; } = 6;

    public double FontSize { get; set; } = 10;

    public bool Bold { get; set; }

    /// <summary>left / center / right。</summary>
    public string Align { get; set; } = "left";

    /// <summary>vertical（縦線）のとき true。線 / 枠で使用。</summary>
    public bool Vertical { get; set; }

    /// <summary>枠線（none / box / underline）。</summary>
    public string? Border { get; set; }

    public FormElement Clone() => (FormElement)MemberwiseClone();
}

/// <summary>明細表の 1 列。</summary>
public sealed class FormTableColumn
{
    /// <summary>見出し。</summary>
    public string? Label { get; set; }

    /// <summary>レポート結果の列名（空なら見出しと同じ）。</summary>
    public string? Field { get; set; }

    public double WidthMm { get; set; } = 30;

    /// <summary>left / center / right。</summary>
    public string Align { get; set; } = "left";

    public FormTableColumn Clone() => (FormTableColumn)MemberwiseClone();
}

/// <summary>明細表（1 行 = レコード。ページをまたぐと自動で分割）。</summary>
public sealed class FormTableSpec
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 186;

    public double RowHeightMm { get; set; } = 6;

    /// <summary>見出し行の背景色（空 = なし）。</summary>
    public string? HeaderBackground { get; set; } = "#F3F5F8";

    public bool ShowHeader { get; set; } = true;

    public List<FormTableColumn> Columns { get; set; } = new();

    /// <summary>0 = 任意。指定すると 1 ページの行数を固定する。</summary>
    public int MaxRowsPerPage { get; set; }

    public FormTableSpec Clone() => new()
    {
        X = X,
        Y = Y,
        Width = Width,
        RowHeightMm = RowHeightMm,
        HeaderBackground = HeaderBackground,
        ShowHeader = ShowHeader,
        MaxRowsPerPage = MaxRowsPerPage,
        Columns = Columns.Select(c => c.Clone()).ToList(),
    };
}

/// <summary>
/// 帳票（SVF 風フォーム）の定義。ヘッダー要素 + 明細表 + フッター要素で 1 ページを構成する。
/// </summary>
public sealed class FormDefinition
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary><see cref="FormCategories"/> のいずれか。</summary>
    public string Category { get; set; } = FormCategories.General;

    public string PaperSize { get; set; } = ReportPapers.A4;

    /// <summary>portrait / landscape。</summary>
    public string Orientation { get; set; } = "portrait";

    public double MarginMm { get; set; } = 10;

    /// <summary>既定フォントサイズ（要素側で上書き可）。</summary>
    public double FontSize { get; set; } = 10;

    /// <summary>ヘッダー領域（明細より上に 1 回だけ描く）。</summary>
    public List<FormElement> Header { get; set; } = new();

    /// <summary>明細表。</summary>
    public FormTableSpec? Table { get; set; }

    /// <summary>フッター領域（各ページの明細の下に描く）。</summary>
    public List<FormElement> Footer { get; set; } = new();

    /// <summary>組み込みテンプレートか（true ならユーザー定義の複製のみ保存）。</summary>
    public bool IsBuiltIn { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public static string NewId() => "f_" + Guid.NewGuid().ToString("N")[..10];

    public FormDefinition Clone() => new()
    {
        Id = "",
        Name = Name,
        Category = Category,
        PaperSize = PaperSize,
        Orientation = Orientation,
        MarginMm = MarginMm,
        FontSize = FontSize,
        Header = Header.Select(e => e.Clone()).ToList(),
        Table = Table?.Clone(),
        Footer = Footer.Select(e => e.Clone()).ToList(),
        IsBuiltIn = false,
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
    };
}

/// <summary>forms.json のルート要素。</summary>
public sealed class FormDefinitionDocument
{
    public int Version { get; set; } = 1;

    public List<FormDefinition> Forms { get; set; } = new();
}

/// <summary>帳票に差し込むデータ（レポート結果 + 自由入力の項目）。</summary>
public sealed class FormData
{
    public string Title { get; init; } = "";

    public string? Org { get; init; }

    public string? Source { get; init; }

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>明細の元データ。</summary>
    public ReportTable? Table { get; init; }

    /// <summary>自由入力の差し込み値（顧客名・見積番号など）。</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>差し込み値をまとめた辞書（共通値 + 自由入力）。</summary>
    public IReadOnlyDictionary<string, string> BuildValues(int page, int pages)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = Title,
            ["ReportName"] = Title,
            ["Org"] = Org ?? "",
            ["Source"] = Source ?? "SfUi",
            ["Date"] = GeneratedAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.CurrentCulture),
            ["DateTime"] = GeneratedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture),
            ["Rows"] = (Table?.RowCount ?? 0).ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            ["Page"] = page.ToString(System.Globalization.CultureInfo.CurrentCulture),
            ["Pages"] = pages.ToString(System.Globalization.CultureInfo.CurrentCulture),
        };

        if (Table is not null)
        {
            for (var i = 0; i < Table.Columns.Count; i++)
            {
                var sum = Table.NumericColumns.Contains(i)
                    ? Table.Rows.Sum(row => TryNumber(i < row.Count ? row[i] : null))
                    : 0;
                values["Total:" + Table.Columns[i]] = sum.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
                values["Sum:" + Table.Columns[i]] = sum.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
                values["Count:" + Table.Columns[i]] = Table.Rows
                    .Count(row => !string.IsNullOrWhiteSpace(i < row.Count ? row[i] : null))
                    .ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
            }
        }

        foreach (var pair in Values)
        {
            values[pair.Key] = pair.Value;
            values["Values:" + pair.Key] = pair.Value;
        }

        return values;
    }

    private static double TryNumber(string? text) =>
        double.TryParse(
            (text ?? "").Replace(",", "", StringComparison.Ordinal).Replace("¥", "", StringComparison.Ordinal)
                .Replace("$", "", StringComparison.Ordinal).Trim(),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
}
