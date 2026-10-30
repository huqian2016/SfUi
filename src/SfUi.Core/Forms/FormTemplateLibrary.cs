namespace SfUi.Core;

/// <summary>テンプレートの明細列指定。</summary>
public readonly record struct FormTemplateColumn(string Label, double WidthMm, string Align = "left");

/// <summary>
/// 帳票テンプレートを組み立てる小さな DSL。
/// 座標は用紙左上からの mm（A4 縦 = 210 × 297）。
/// </summary>
public sealed class FormTemplateBuilder
{
    private readonly FormDefinition _form;

    public FormTemplateBuilder(string id, string name, string category, string paper = ReportPapers.A4, string orientation = "portrait", double margin = 10, string region = FormRegions.Japan)
    {
        _form = new FormDefinition
        {
            Id = id,
            Name = name,
            Category = category,
            Region = region,
            PaperSize = paper,
            Orientation = orientation,
            MarginMm = margin,
            IsBuiltIn = true,
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        };
    }

    /// <summary>ヘッダー要素（文字）。</summary>
    public FormTemplateBuilder H(string text, double x, double y, double width, double height = 6, double fontSize = 0, bool bold = false, string align = "left", string? border = null)
    {
        _form.Header.Add(Text(text, x, y, width, height, fontSize, bold, align, border));
        return this;
    }

    /// <summary>フッター要素（文字）。</summary>
    public FormTemplateBuilder F(string text, double x, double y, double width, double height = 6, double fontSize = 0, bool bold = false, string align = "left", string? border = null)
    {
        _form.Footer.Add(Text(text, x, y, width, height, fontSize, bold, align, border));
        return this;
    }

    /// <summary>水平線。</summary>
    public FormTemplateBuilder Rule(double y, double x = 10, double width = 190)
    {
        _form.Header.Add(new FormElement { Type = FormElementTypes.Line, X = x, Y = y, Width = width, Height = 0.3 });
        return this;
    }

    /// <summary>垂直線。</summary>
    public FormTemplateBuilder VRule(double x, double y, double height)
    {
        _form.Header.Add(new FormElement { Type = FormElementTypes.Line, X = x, Y = y, Width = 0.3, Height = height, Vertical = true });
        return this;
    }

    /// <summary>枠。</summary>
    public FormTemplateBuilder Frame(double x, double y, double width, double height)
    {
        _form.Header.Add(new FormElement { Type = FormElementTypes.Box, X = x, Y = y, Width = width, Height = height });
        return this;
    }

    /// <summary>明細表。</summary>
    public FormTemplateBuilder Table(double x, double y, double width, double rowHeight, params FormTemplateColumn[] columns)
    {
        _form.Table = new FormTableSpec
        {
            X = x,
            Y = y,
            Width = width,
            RowHeightMm = rowHeight,
            ShowHeader = true,
            HeaderBackground = "#F3F5F8",
            Columns = columns.Select(c => new FormTableColumn
            {
                Label = c.Label,
                Field = c.Label,
                WidthMm = c.WidthMm,
                Align = c.Align,
            }).ToList(),
        };
        return this;
    }

    /// <summary>明細表の見出し行を出さない / 行数を固定する。</summary>
    public FormTemplateBuilder TableOptions(bool showHeader = true, int maxRowsPerPage = 0)
    {
        if (_form.Table is { } table)
        {
            table.ShowHeader = showHeader;
            table.MaxRowsPerPage = maxRowsPerPage;
        }

        return this;
    }

    /// <summary>帳票定義を作る。</summary>
    public FormDefinition Build() => _form;

    private FormElement Text(string text, double x, double y, double width, double height, double fontSize, bool bold, string align, string? border) => new()
    {
        Type = FormElementTypes.Text,
        Text = text,
        X = x,
        Y = y,
        Width = width,
        Height = height,
        FontSize = fontSize,
        Bold = bold,
        Align = align,
        Border = border,
    };
}

/// <summary>
/// 組み込み帳票テンプレート（業界別の見積書・注文書・納品書・請求書・領収書、経理書類）。
/// テンプレートの文言は「文書の内容（データ）」で、ユーザーが複製して編集する前提。
/// </summary>
public static partial class FormTemplateLibrary
{
    /// <summary>組み込みテンプレートの一覧。</summary>
    public static IReadOnlyList<FormDefinition> All { get; } = BuildAll();

    /// <summary>英語圏（米国 / 英国 / EU / 豪州 / NZ / カナダ）のテンプレート。</summary>
    public static IReadOnlyList<FormDefinition> International { get; } =
        All.Where(f => !string.Equals(f.Region, FormRegions.Japan, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>カテゴリで絞り込む。</summary>
    public static IReadOnlyList<FormDefinition> ByCategory(string? category) =>
        string.IsNullOrWhiteSpace(category)
            ? All
            : All.Where(f => string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>地域で絞り込む（空 = すべて）。</summary>
    public static IReadOnlyList<FormDefinition> ByRegion(string? region) =>
        string.IsNullOrWhiteSpace(region)
            ? All
            : All.Where(f => string.Equals(f.Region, region, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Id で 1 件取得する。</summary>
    public static FormDefinition? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));

    private static List<FormDefinition> BuildAll()
    {
        var forms = new List<FormDefinition>
        {
            EstimateManufacturing(),
            EstimateConstruction(),
            EstimateIt(),
            EstimateRetail(),
            OrderManufacturing(),
            PurchaseOrder(),
            DeliveryNote(),
            InvoiceService(),
            InvoiceConstruction(),
            Receipt(),
            ReceiptQualifiedInvoice(),
            Payslip(),
            BonusSlip(),
            WithholdingTaxTable(),
            PaymentStatement(),
            SalesLedger(),
            GeneralList(),
        };

        forms.AddRange(BuildInternational());
        return forms;
    }

    // ── 見積書 ──

    private static FormDefinition EstimateManufacturing() => new FormTemplateBuilder("t_estimate_manufacturing", "見積書（製造業）", FormCategories.Estimate)
        .H("御見積書", 60, 12, 90, 12, 22, bold: true, align: "center")
        .H("見積番号: {Values:QuoteNo}", 130, 28, 70, 5, 9, align: "right")
        .H("発行日: {Date}", 130, 34, 70, 5, 9, align: "right")
        .H("宛先: {Values:CustomerName} 御中", 10, 30, 105, 6, 11, bold: true)
        .H("件名: {Values:Subject}", 10, 38, 105, 5, 10)
        .H("自社: {Values:CompanyName}", 10, 46, 80, 5, 9)
        .H("{Values:CompanyAddress}", 10, 51, 80, 5, 9)
        .H("TEL: {Values:CompanyTel}", 10, 56, 80, 5, 9)
        .H("担当: {Values:StaffName}", 130, 46, 70, 5, 9, align: "right")
        .H("納期: {Values:DeliveryDate}", 130, 51, 70, 5, 9, align: "right")
        .H("有効期限: {Values:ValidUntil}", 130, 56, 70, 5, 9, align: "right")
        .Rule(64)
        .H("下記のとおりお見積り申し上げます。", 10, 68, 120, 5, 10)
        .H("御見積金額（税込）", 110, 68, 90, 8, 10, align: "right")
        .H("{Total:金額} 円", 110, 74, 90, 10, 16, bold: true, align: "right")
        .Table(10, 90, 190, 7,
            new FormTemplateColumn("品名", 60),
            new FormTemplateColumn("数量", 20, "right"),
            new FormTemplateColumn("単位", 16, "center"),
            new FormTemplateColumn("単価", 30, "right"),
            new FormTemplateColumn("金額", 34, "right"))
        .F("小計: {Total:金額}", 110, 240, 90, 6, 10, align: "right")
        .F("消費税（10%）: {Values:TaxAmount}", 110, 246, 90, 6, 10, align: "right")
        .F("合計: {Values:TotalAmount}", 110, 252, 90, 8, 12, bold: true, align: "right")
        .F("備考: {Values:Remark}", 10, 250, 95, 16, 9)
        .F("※ 本見積書の有効期限は上記のとおりです。", 10, 276, 120, 5, 8)
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition EstimateConstruction() => new FormTemplateBuilder("t_estimate_construction", "見積書（建設・工事）", FormCategories.Estimate)
        .H("御見積書", 60, 12, 90, 12, 22, bold: true, align: "center")
        .H("工事名: {Values:ProjectName}", 10, 28, 120, 6, 11, bold: true)
        .H("見積番号: {Values:QuoteNo}", 130, 28, 70, 5, 9, align: "right")
        .H("発行日: {Date}", 130, 34, 70, 5, 9, align: "right")
        .H("宛先: {Values:CustomerName} 様", 10, 36, 105, 6, 11)
        .H("工事場所: {Values:SiteAddress}", 10, 43, 190, 5, 9)
        .H("工期: {Values:Period}", 10, 49, 190, 5, 9)
        .H("内訳明細", 10, 58, 60, 6, 10, bold: true)
        .Rule(66)
        .Table(10, 70, 190, 7,
            new FormTemplateColumn("工種", 40),
            new FormTemplateColumn("仕様・規格", 70),
            new FormTemplateColumn("数量", 18, "right"),
            new FormTemplateColumn("単位", 14, "center"),
            new FormTemplateColumn("金額", 48, "right"))
        .F("直接工事費計: {Total:金額}", 110, 236, 90, 6, 10, align: "right")
        .F("諸経費: {Values:Expenses}", 110, 242, 90, 6, 10, align: "right")
        .F("消費税: {Values:TaxAmount}", 110, 248, 90, 6, 10, align: "right")
        .F("合計金額: {Values:TotalAmount}", 110, 254, 90, 8, 12, bold: true, align: "right")
        .F("備考: {Values:Remark}", 10, 250, 95, 20, 9)
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition EstimateIt() => new FormTemplateBuilder("t_estimate_it", "見積書（IT・システム）", FormCategories.Estimate)
        .H("御見積書", 60, 12, 90, 12, 22, bold: true, align: "center")
        .H("件名: {Values:Subject}", 10, 28, 130, 6, 11, bold: true)
        .H("見積番号: {Values:QuoteNo}", 145, 28, 55, 5, 9, align: "right")
        .H("発行日: {Date}", 145, 34, 55, 5, 9, align: "right")
        .H("宛先: {Values:CustomerName} 御中", 10, 36, 120, 6, 11)
        .H("担当: {Values:StaffName}", 145, 40, 55, 5, 9, align: "right")
        .H("概算費用（税抜）", 110, 50, 90, 6, 10, align: "right")
        .H("{Total:金額} 円", 110, 56, 90, 12, 18, bold: true, align: "right")
        .Rule(72)
        .H("作業内訳", 10, 76, 60, 6, 10, bold: true)
        .Table(10, 84, 190, 7,
            new FormTemplateColumn("工程 / 作業項目", 70),
            new FormTemplateColumn("内容", 60),
            new FormTemplateColumn("工数(h)", 20, "right"),
            new FormTemplateColumn("金額", 40, "right"))
        .F("小計: {Total:金額}", 110, 240, 90, 6, 10, align: "right")
        .F("消費税: {Values:TaxAmount}", 110, 246, 90, 6, 10, align: "right")
        .F("合計: {Values:TotalAmount}", 110, 252, 90, 8, 12, bold: true, align: "right")
        .F("前提条件: {Values:Assumptions}", 10, 244, 95, 30, 9)
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition EstimateRetail() => new FormTemplateBuilder("t_estimate_retail", "見積書（小売・店舗）", FormCategories.Estimate)
        .H("お見積書", 60, 12, 90, 12, 20, bold: true, align: "center")
        .H("宛先: {Values:CustomerName} 様", 10, 30, 120, 6, 11)
        .H("{Date}", 150, 30, 50, 5, 9, align: "right")
        .H("お見積番号: {Values:QuoteNo}", 10, 38, 90, 5, 9)
        .H("店舗: {Values:ShopName}", 10, 44, 90, 5, 9)
        .Rule(54)
        .Table(10, 62, 190, 8,
            new FormTemplateColumn("商品名", 80),
            new FormTemplateColumn("数量", 20, "right"),
            new FormTemplateColumn("単価", 40, "right"),
            new FormTemplateColumn("金額", 50, "right"))
        .F("小計: {Total:金額}", 110, 240, 90, 6, 10, align: "right")
        .F("税: {Values:TaxAmount}", 110, 246, 90, 6, 10, align: "right")
        .F("合計: {Values:TotalAmount}", 110, 252, 90, 8, 12, bold: true, align: "right")
        .F("※ 価格は予告なく変更となる場合があります。", 10, 254, 120, 5, 8)
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    // ── 注文書 / 発注書 ──

    private static FormDefinition OrderManufacturing() => new FormTemplateBuilder("t_order_manufacturing", "注文書（製造業）", FormCategories.Order)
        .H("注文書", 60, 12, 90, 12, 22, bold: true, align: "center")
        .H("注文書番号: {Values:OrderNo}", 10, 30, 90, 5, 10)
        .H("注文日: {Date}", 150, 30, 50, 5, 9, align: "right")
        .H("納入希望日: {Values:DeliveryDate}", 150, 36, 50, 5, 9, align: "right")
        .H("発注者: {Values:CompanyName}", 10, 38, 90, 5, 9)
        .H("納入先: {Values:ShipTo}", 10, 46, 120, 5, 9)
        .Rule(56)
        .H("下記のとおり注文します。", 10, 60, 120, 5, 10)
        .Table(10, 72, 190, 7,
            new FormTemplateColumn("品番", 36),
            new FormTemplateColumn("品名", 62),
            new FormTemplateColumn("数量", 18, "right"),
            new FormTemplateColumn("単価", 34, "right"),
            new FormTemplateColumn("金額", 40, "right"))
        .F("合計: {Total:金額}", 110, 240, 90, 8, 12, bold: true, align: "right")
        .F("減価条件: {Values:Terms}", 10, 244, 95, 12, 9)
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition PurchaseOrder() => new FormTemplateBuilder("t_purchase_order", "発注書（購買）", FormCategories.Order)
        .H("発注書", 60, 12, 90, 12, 22, bold: true, align: "center")
        .H("発注番号: {Values:OrderNo}", 130, 28, 70, 5, 9, align: "right")
        .H("発注日: {Date}", 130, 34, 70, 5, 9, align: "right")
        .H("仕入先: {Values:CustomerName} 御中", 10, 30, 105, 6, 11, bold: true)
        .H("発注元: {Values:CompanyName}", 10, 40, 100, 5, 9)
        .H("担当: {Values:StaffName}", 10, 46, 100, 5, 9)
        .Rule(54)
        .Table(10, 66, 190, 7,
            new FormTemplateColumn("品名 / 型番", 76),
            new FormTemplateColumn("数量", 18, "right"),
            new FormTemplateColumn("単価", 34, "right"),
            new FormTemplateColumn("金額", 40, "right"),
            new FormTemplateColumn("納期", 22, "center"))
        .F("発注金額: {Total:金額}", 110, 240, 90, 8, 12, bold: true, align: "right")
        .F("納品場所: {Values:ShipTo}", 10, 240, 95, 5, 9)
        .F("支払条件: {Values:Terms}", 10, 246, 95, 5, 9)
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition DeliveryNote() => new FormTemplateBuilder("t_delivery_note", "納品書", FormCategories.Delivery)
        .H("納品書", 60, 12, 90, 12, 22, bold: true, align: "center")
        .H("宛先: {Values:CustomerName} 御中", 10, 30, 120, 6, 11, bold: true)
        .H("納品書番号: {Values:OrderNo}", 140, 30, 60, 5, 9, align: "right")
        .H("納品日: {Date}", 140, 36, 60, 5, 9, align: "right")
        .H("納入場所: {Values:ShipTo}", 10, 38, 120, 5, 9)
        .H("ご担当: {Values:StaffName} 様", 10, 44, 120, 5, 9)
        .Rule(54)
        .Table(10, 62, 190, 7,
            new FormTemplateColumn("品名 / 型番", 80),
            new FormTemplateColumn("数量", 20, "right"),
            new FormTemplateColumn("単位", 16, "center"),
            new FormTemplateColumn("摘要", 74))
        .F("合計数量: {Total:数量}", 110, 240, 90, 6, 10, align: "right")
        .F("※ 上記のとおり納品いたしました。ご確認のほどお願い申し上げます。", 10, 240, 110, 8, 9)
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    // ── 請求書 ──

    private static FormDefinition InvoiceService() => new FormTemplateBuilder("t_invoice_service", "請求書（サービス）", FormCategories.Invoice)
        .H("請求書", 60, 12, 90, 12, 22, bold: true, align: "center")
        .H("請求番号: {Values:InvoiceNo}", 130, 28, 70, 5, 9, align: "right")
        .H("請求日: {Date}", 130, 34, 70, 5, 9, align: "right")
        .H("お支払期限: {Values:DueDate}", 130, 40, 70, 5, 9, align: "right")
        .H("宛先: {Values:CustomerName} 御中", 10, 30, 105, 6, 11, bold: true)
        .H("ご請求金額（税込）", 10, 52, 90, 6, 10)
        .H("{Values:TotalAmount} 円", 10, 58, 100, 12, 18, bold: true)
        .H("振込先: {Values:BankAccount}", 10, 76, 100, 5, 9)
        .H("件名: {Values:Subject}", 10, 82, 190, 5, 9)
        .Rule(92)
        .Table(10, 100, 190, 7,
            new FormTemplateColumn("品目", 86),
            new FormTemplateColumn("数量", 18, "right"),
            new FormTemplateColumn("単価", 36, "right"),
            new FormTemplateColumn("金額", 50, "right"))
        .F("小計: {Total:金額}", 110, 238, 90, 6, 10, align: "right")
        .F("消費税（10%）: {Values:TaxAmount}", 110, 244, 90, 6, 10, align: "right")
        .F("ご請求金額: {Values:TotalAmount}", 110, 250, 90, 8, 12, bold: true, align: "right")
        .F("備考: {Values:Remark}", 10, 240, 95, 22, 9)
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition InvoiceConstruction() => new FormTemplateBuilder("t_invoice_construction", "請求書（建設・出来高）", FormCategories.Invoice)
        .H("出来高請求書", 55, 12, 100, 12, 20, bold: true, align: "center")
        .H("工事名: {Values:ProjectName}", 10, 30, 120, 6, 11, bold: true)
        .H("請求番号: {Values:InvoiceNo}", 140, 30, 60, 5, 9, align: "right")
        .H("請求日: {Date}", 140, 36, 60, 5, 9, align: "right")
        .H("宛先: {Values:CustomerName} 御中", 10, 38, 120, 6, 11)
        .H("今回出来高: {Values:Progress}", 10, 46, 120, 5, 9)
        .H("ご請求金額（税込）", 10, 56, 100, 6, 10)
        .H("{Values:TotalAmount} 円", 10, 62, 110, 12, 18, bold: true)
        .Rule(78)
        .Table(10, 88, 190, 7,
            new FormTemplateColumn("工種", 44),
            new FormTemplateColumn("内容", 66),
            new FormTemplateColumn("出来高(%)", 22, "right"),
            new FormTemplateColumn("金額", 58, "right"))
        .F("今回請求額: {Total:金額}", 110, 240, 90, 8, 12, bold: true, align: "right")
        .F("累計請求額: {Values:CumulativeAmount}", 110, 248, 90, 6, 10, align: "right")
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    // ── 領収書 ──

    private static FormDefinition Receipt() => new FormTemplateBuilder("t_receipt", "領収書", FormCategories.Receipt)
        .H("領　収　書", 60, 16, 90, 14, 24, bold: true, align: "center")
        .H("No. {Values:ReceiptNo}", 145, 20, 55, 5, 9, align: "right")
        .H("発行日: {Date}", 145, 26, 55, 5, 9, align: "right")
        .H("宛名: {Values:CustomerName} 様", 20, 52, 170, 8, 13, bold: true)
        .Rule(64, 20, 170)
        .H("金額", 20, 74, 40, 6, 11)
        .H("¥ {Values:TotalAmount}", 60, 66, 130, 18, 22, bold: true, align: "center")
        .Rule(92, 20, 170)
        .H("但し  {Values:Subject} として", 20, 98, 170, 6, 11)
        .H("上記正に領収いたしました。", 20, 112, 170, 6, 11)
        .H("内訳", 20, 136, 60, 6, 10, bold: true)
        .Table(20, 144, 170, 7,
            new FormTemplateColumn("摘要", 110),
            new FormTemplateColumn("金額", 60, "right"))
        .F("税抜金額: {Values:TaxExcluded}", 110, 236, 80, 5, 9, align: "right")
        .F("消費税額: {Values:TaxAmount}", 110, 242, 80, 5, 9, align: "right")
        .F("発行者: {Values:CompanyName}", 20, 240, 85, 5, 9)
        .F("{Values:CompanyAddress}", 20, 246, 85, 5, 8)
        .F("TEL: {Values:CompanyTel}", 20, 252, 85, 5, 8)
        .F("収入印紙", 20, 200, 30, 5, 8, border: "box")
        .Build();

    private static FormDefinition ReceiptQualifiedInvoice() => new FormTemplateBuilder("t_receipt_qualified", "領収書（インボイス対応）", FormCategories.Receipt)
        .H("領　収　書", 60, 14, 90, 12, 22, bold: true, align: "center")
        .H("適格簡易請求書", 60, 26, 90, 5, 10, align: "center")
        .H("No. {Values:ReceiptNo}", 145, 18, 55, 5, 9, align: "right")
        .H("発行日: {Date}", 145, 24, 55, 5, 9, align: "right")
        .H("宛名: {Values:CustomerName} 様", 15, 42, 180, 8, 13, bold: true)
        .Rule(54, 15, 180)
        .H("¥ {Values:TotalAmount}", 15, 60, 180, 16, 20, bold: true, align: "center")
        .H("（税込）", 15, 76, 180, 5, 9, align: "center")
        .Rule(84, 15, 180)
        .H("但し  {Values:Subject} として", 15, 90, 180, 6, 10)
        .H("税率区分", 15, 104, 40, 5, 9, bold: true)
        .H("対象金額", 70, 104, 40, 5, 9, bold: true, align: "right")
        .H("消費税額", 130, 104, 40, 5, 9, bold: true, align: "right")
        .H("10%", 15, 110, 40, 5, 10)
        .H("{Values:Taxable10}", 70, 110, 40, 5, 10, align: "right")
        .H("{Values:Tax10}", 130, 110, 40, 5, 10, align: "right")
        .H("8%（軽減）", 15, 116, 40, 5, 10)
        .H("{Values:Taxable8}", 70, 116, 40, 5, 10, align: "right")
        .H("{Values:Tax8}", 130, 116, 40, 5, 10, align: "right")
        .H("登録番号: {Values:InvoiceRegistrationNo}", 15, 130, 180, 5, 10, bold: true)
        .Table(15, 142, 180, 7,
            new FormTemplateColumn("摘要", 120),
            new FormTemplateColumn("金額", 60, "right"))
        .F("発行者: {Values:CompanyName}", 15, 238, 90, 5, 9)
        .F("{Values:CompanyAddress}", 15, 244, 90, 5, 8)
        .F("TEL: {Values:CompanyTel}", 15, 250, 90, 5, 8)
        .F("Page {Page} / {Pages}", 145, 282, 50, 5, 8, align: "right")
        .Build();

    // ── 経理書類 ──

    private static FormDefinition Payslip() => new FormTemplateBuilder("t_payslip", "給与明細", FormCategories.Payroll)
        .H("給与明細書", 55, 12, 100, 12, 20, bold: true, align: "center")
        .H("支給年月: {Values:PayMonth}", 130, 12, 70, 5, 9, align: "right")
        .H("社員番号: {Values:EmployeeNo}", 10, 30, 60, 5, 10)
        .H("氏名: {Values:EmployeeName} 様", 70, 30, 100, 6, 12, bold: true)
        .H("所属: {Values:Department}", 10, 37, 90, 5, 9)
        .Rule(46)
        .H("支給", 10, 52, 60, 6, 11, bold: true)
        .H("控除", 105, 52, 60, 6, 11, bold: true)
        .Table(10, 60, 90, 6,
            new FormTemplateColumn("支給項目", 60),
            new FormTemplateColumn("金額", 30, "right"))
        .TableOptions(showHeader: true, maxRowsPerPage: 12)
        .F("差引支給額: {Values:NetPay}", 10, 236, 190, 10, 14, bold: true, align: "right")
        .F("控除合計: {Values:DeductionTotal}", 105, 200, 92, 6, 10, align: "right")
        .F("支給合計: {Values:GrossTotal}", 10, 200, 92, 6, 10, align: "right")
        .F("※ 内容に誤りがある場合は人事部までご連絡ください。", 10, 252, 190, 5, 8)
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition BonusSlip() => new FormTemplateBuilder("t_bonus_slip", "賞与明細", FormCategories.Payroll)
        .H("賞与明細書", 55, 12, 100, 12, 20, bold: true, align: "center")
        .H("支給日: {Date}", 130, 12, 70, 5, 9, align: "right")
        .H("社員番号: {Values:EmployeeNo}", 10, 30, 60, 5, 10)
        .H("氏名: {Values:EmployeeName} 様", 70, 30, 100, 6, 12, bold: true)
        .H("対象期間: {Values:Period}", 10, 37, 120, 5, 9)
        .Rule(46)
        .Table(10, 60, 190, 6,
            new FormTemplateColumn("項目", 60),
            new FormTemplateColumn("計算基礎", 60),
            new FormTemplateColumn("金額", 70, "right"))
        .TableOptions(showHeader: true, maxRowsPerPage: 14)
        .F("賞与支給額: {Values:GrossTotal}", 10, 220, 92, 8, 12, bold: true, align: "right")
        .F("控除合計: {Values:DeductionTotal}", 105, 220, 92, 8, 12, bold: true, align: "right")
        .F("差引支給額: {Values:NetPay}", 10, 232, 190, 10, 14, bold: true, align: "right")
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition WithholdingTaxTable() => new FormTemplateBuilder("t_withholding_tax", "源泉徴収税額表", FormCategories.Tax, ReportPapers.A3, "landscape")
        .H("給与所得の源泉徴収税額表（月額）", 10, 10, 250, 10, 16, bold: true, align: "center")
        .H("適用: {Values:PayMonth} / {Values:TaxClass}", 200, 22, 130, 5, 9, align: "right")
        .H("社会保険料等控除後の給与等の金額", 10, 26, 120, 5, 9)
        .Table(10, 34, 397, 5.5,
            new FormTemplateColumn("以上", 40, "right"),
            new FormTemplateColumn("未満", 40, "right"),
            new FormTemplateColumn("甲欄（0人）", 40, "right"),
            new FormTemplateColumn("甲欄（1人）", 40, "right"),
            new FormTemplateColumn("甲欄（2人）", 40, "right"),
            new FormTemplateColumn("甲欄（3人）", 40, "right"),
            new FormTemplateColumn("甲欄（4人）", 40, "right"),
            new FormTemplateColumn("甲欄（5人）", 40, "right"),
            new FormTemplateColumn("甲欄（6人）", 40, "right"),
            new FormTemplateColumn("甲欄（7人以上）", 40, "right"),
            new FormTemplateColumn("乙欄", 37, "right"))
        .TableOptions(showHeader: true, maxRowsPerPage: 40)
        .F("※ 税額は国税庁の源泉徴収税額表（月額表）に基づきます。端数は1円未満切り捨て。", 10, 272, 300, 5, 8)
        .F("Page {Page} / {Pages}", 360, 272, 47, 5, 8, align: "right")
        .Build();

    private static FormDefinition PaymentStatement() => new FormTemplateBuilder("t_payment_statement", "支払調書", FormCategories.Tax)
        .H("支払調書", 60, 12, 90, 12, 20, bold: true, align: "center")
        .H("提出者: {Values:CompanyName}", 10, 30, 100, 5, 9)
        .H("提出者住所: {Values:CompanyAddress}", 10, 35, 100, 5, 9)
        .H("年度: {Values:FiscalYear}", 150, 30, 50, 5, 9, align: "right")
        .H("作成日: {Date}", 150, 36, 50, 5, 9, align: "right")
        .Rule(46)
        .H("支払先の名称: {Values:CustomerName}", 10, 54, 190, 6, 10)
        .H("支払先の住所: {Values:ShipTo}", 10, 61, 190, 5, 9)
        .H("区分: {Values:PaymentKind}", 10, 68, 190, 5, 9)
        .Table(10, 82, 190, 7,
            new FormTemplateColumn("支払年月日", 40, "center"),
            new FormTemplateColumn("支払金額", 50, "right"),
            new FormTemplateColumn("源泉徴収税額", 50, "right"),
            new FormTemplateColumn("摘要", 50))
        .F("支払金額合計: {Total:支払金額}", 10, 240, 190, 8, 12, bold: true, align: "right")
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition SalesLedger() => new FormTemplateBuilder("t_sales_ledger", "売掛金台帳", FormCategories.Statement, ReportPapers.A4, "landscape")
        .H("売掛金台帳", 10, 10, 120, 10, 16, bold: true)
        .H("対象期間: {Values:Period}", 200, 10, 87, 5, 9, align: "right")
        .H("作成日: {Date}", 200, 16, 87, 5, 9, align: "right")
        .H("Page {Page} / {Pages}", 200, 22, 87, 5, 8, align: "right")
        .Table(10, 32, 277, 6,
            new FormTemplateColumn("取引先", 70),
            new FormTemplateColumn("請求日", 28, "center"),
            new FormTemplateColumn("入金日", 28, "center"),
            new FormTemplateColumn("売上金額", 45, "right"),
            new FormTemplateColumn("入金額", 45, "right"),
            new FormTemplateColumn("残高", 45, "right"),
            new FormTemplateColumn("備考", 16))
        .TableOptions(showHeader: true, maxRowsPerPage: 28)
        .F("売上合計: {Total:売上金額}", 10, 196, 277, 6, 10, align: "right")
        .Build();

    private static FormDefinition GeneralList() => new FormTemplateBuilder("t_general", "汎用帳票（一覧）", FormCategories.General)
        .H("{Title}", 10, 12, 190, 10, 16, bold: true)
        .H("出力日: {Date}   対象: {Org}", 10, 24, 190, 5, 9)
        .H("件数: {Rows}", 150, 30, 50, 5, 9, align: "right")
        .Rule(38)
        .Table(10, 46, 190, 6.5)
        .F("Page {Page} / {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();
}
