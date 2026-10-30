namespace SfUi.Core;

/// <summary>
/// 英語圏（米国 / 英国 / EU / 豪州 / ニュージーランド / カナダ）の帳票テンプレート。
/// 請求書の必須項目（EU の VAT 番号・逆課税表示、英国の VAT 内訳、豪州の ABN と GST、
/// NZ の GST 番号、米国の Sales Tax など）をレイアウトに含めている。
/// </summary>
public static partial class FormTemplateLibrary
{
    private static List<FormDefinition> BuildInternational() => new()
    {
        UsInvoice(),
        UsPurchaseOrder(),
        UsQuote(),
        UsPackingSlip(),
        UsPayStub(),
        UsStatement(),
        UkVatInvoice(),
        UkCreditNote(),
        EuVatInvoice(),
        EuProformaInvoice(),
        EuCreditNote(),
        AuTaxInvoice(),
        AuQuote(),
        AuPayslip(),
        NzTaxInvoice(),
        NzPayslip(),
        CaInvoice(),
    };

    // ── 米国 ──

    private static FormDefinition UsInvoice() => new FormTemplateBuilder("t_us_invoice", "Invoice (US)", FormCategories.Invoice, region: FormRegions.UnitedStates)
        .H("INVOICE", 120, 12, 80, 10, 22, bold: true, align: "right")
        .H("{Values:CompanyName}", 10, 12, 105, 6, 12, bold: true)
        .H("{Values:CompanyAddress}", 10, 19, 105, 5, 9)
        .H("{Values:CompanyCityLine}", 10, 25, 105, 5, 9)
        .H("Phone: {Values:CompanyPhone}", 10, 31, 105, 5, 9)
        .H("Email: {Values:CompanyEmail}", 10, 37, 105, 5, 9)
        .H("Invoice No.", 120, 28, 40, 5, 9, align: "right")
        .H("{Values:InvoiceNo}", 162, 28, 38, 5, 9, align: "right")
        .H("Invoice Date", 120, 34, 40, 5, 9, align: "right")
        .H("{Date}", 162, 34, 38, 5, 9, align: "right")
        .H("Due Date", 120, 40, 40, 5, 9, align: "right")
        .H("{Values:DueDate}", 162, 40, 38, 5, 9, align: "right")
        .H("Terms", 120, 46, 40, 5, 9, align: "right")
        .H("{Values:Terms}", 162, 46, 38, 5, 9, align: "right")
        .H("PO Number", 120, 52, 40, 5, 9, align: "right")
        .H("{Values:PoNumber}", 162, 52, 38, 5, 9, align: "right")
        .Rule(62)
        .H("BILL TO", 10, 66, 90, 5, 9, bold: true)
        .H("SHIP TO", 105, 66, 95, 5, 9, bold: true)
        .H("{Values:BillToName}", 10, 72, 90, 5, 10)
        .H("{Values:BillToAddress}", 10, 78, 90, 10, 9)
        .H("{Values:ShipToName}", 105, 72, 95, 5, 10)
        .H("{Values:ShipToAddress}", 105, 78, 95, 10, 9)
        .Table(10, 94, 190, 7,
            new FormTemplateColumn("Description", 92),
            new FormTemplateColumn("Qty", 20, "right"),
            new FormTemplateColumn("Unit Price", 34, "right"),
            new FormTemplateColumn("Amount", 44, "right"))
        .F("Subtotal", 110, 214, 50, 5, 9, align: "right")
        .F("{Total:Amount}", 162, 214, 38, 5, 9, align: "right")
        .F("Sales Tax ({Values:TaxRate})", 110, 220, 50, 5, 9, align: "right")
        .F("{Values:SalesTax}", 162, 220, 38, 5, 9, align: "right")
        .F("Shipping / Handling", 110, 226, 50, 5, 9, align: "right")
        .F("{Values:Shipping}", 162, 226, 38, 5, 9, align: "right")
        .F("TOTAL DUE", 110, 233, 50, 7, 11, bold: true, align: "right")
        .F("{Values:TotalDue}", 162, 233, 38, 7, 11, bold: true, align: "right")
        .F("Amount Paid", 110, 241, 50, 5, 9, align: "right")
        .F("{Values:AmountPaid}", 162, 241, 38, 5, 9, align: "right")
        .F("BALANCE DUE", 110, 248, 50, 7, 11, bold: true, align: "right")
        .F("{Values:BalanceDue}", 162, 248, 38, 7, 11, bold: true, align: "right")
        .F("REMITTANCE / PAYMENT DETAILS", 10, 214, 95, 5, 9, bold: true)
        .F("{Values:PaymentDetails}", 10, 220, 95, 18, 9)
        .F("Notes: {Values:Notes}", 10, 244, 95, 18, 9)
        .F("Thank you for your business.", 10, 264, 120, 5, 9)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition UsPurchaseOrder() => new FormTemplateBuilder("t_us_purchase_order", "Purchase Order (US)", FormCategories.Order, region: FormRegions.UnitedStates)
        .H("PURCHASE ORDER", 10, 12, 120, 10, 18, bold: true)
        .H("{Values:CompanyName}", 10, 24, 110, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 110, 5, 9)
        .H("PO Number", 130, 12, 30, 5, 9, align: "right")
        .H("{Values:PoNumber}", 162, 12, 38, 5, 9, align: "right")
        .H("Order Date", 130, 18, 30, 5, 9, align: "right")
        .H("{Date}", 162, 18, 38, 5, 9, align: "right")
        .H("Terms", 130, 24, 30, 5, 9, align: "right")
        .H("{Values:Terms}", 162, 24, 38, 5, 9, align: "right")
        .H("Ship Via", 130, 30, 30, 5, 9, align: "right")
        .H("{Values:ShipVia}", 162, 30, 38, 5, 9, align: "right")
        .Rule(40)
        .H("VENDOR", 10, 44, 90, 5, 9, bold: true)
        .H("SHIP TO", 105, 44, 95, 5, 9, bold: true)
        .H("{Values:VendorName}", 10, 50, 90, 5, 10)
        .H("{Values:VendorAddress}", 10, 56, 90, 10, 9)
        .H("{Values:ShipToName}", 105, 50, 95, 5, 10)
        .H("{Values:ShipToAddress}", 105, 56, 95, 10, 9)
        .Table(10, 74, 190, 7,
            new FormTemplateColumn("Item No.", 30),
            new FormTemplateColumn("Description", 80),
            new FormTemplateColumn("Qty", 18, "right"),
            new FormTemplateColumn("Unit Cost", 28, "right"),
            new FormTemplateColumn("Amount", 34, "right"))
        .F("Subtotal", 110, 226, 50, 5, 9, align: "right")
        .F("{Total:Amount}", 162, 226, 38, 5, 9, align: "right")
        .F("Sales Tax", 110, 232, 50, 5, 9, align: "right")
        .F("{Values:SalesTax}", 162, 232, 38, 5, 9, align: "right")
        .F("Shipping", 110, 238, 50, 5, 9, align: "right")
        .F("{Values:Shipping}", 162, 238, 38, 5, 9, align: "right")
        .F("PO TOTAL", 110, 245, 50, 7, 11, bold: true, align: "right")
        .F("{Values:TotalAmount}", 162, 245, 38, 7, 11, bold: true, align: "right")
        .F("Special instructions: {Values:Notes}", 10, 226, 95, 16, 9)
        .F("Authorized by: ______________________", 10, 250, 95, 6, 9)
        .F("Please confirm receipt of this order and the expected ship date.", 10, 264, 150, 5, 8)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition UsQuote() => new FormTemplateBuilder("t_us_quote", "Quote / Estimate (US)", FormCategories.Estimate, region: FormRegions.UnitedStates)
        .H("QUOTE", 10, 12, 120, 10, 20, bold: true)
        .H("{Values:CompanyName}", 10, 24, 110, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 110, 5, 9)
        .H("Quote No.", 130, 12, 30, 5, 9, align: "right")
        .H("{Values:QuoteNo}", 162, 12, 38, 5, 9, align: "right")
        .H("Date", 130, 18, 30, 5, 9, align: "right")
        .H("{Date}", 162, 18, 38, 5, 9, align: "right")
        .H("Valid Until", 130, 24, 30, 5, 9, align: "right")
        .H("{Values:ValidUntil}", 162, 24, 38, 5, 9, align: "right")
        .Rule(40)
        .H("PREPARED FOR", 10, 44, 90, 5, 9, bold: true)
        .H("PROJECT", 105, 44, 95, 5, 9, bold: true)
        .H("{Values:CustomerName}", 10, 50, 90, 5, 10)
        .H("{Values:CustomerAddress}", 10, 56, 90, 10, 9)
        .H("{Values:ProjectName}", 105, 50, 95, 5, 10)
        .H("{Values:ProjectScope}", 105, 56, 95, 10, 9)
        .Table(10, 74, 190, 7,
            new FormTemplateColumn("Description", 100),
            new FormTemplateColumn("Qty", 20, "right"),
            new FormTemplateColumn("Rate", 30, "right"),
            new FormTemplateColumn("Amount", 40, "right"))
        .F("Subtotal", 110, 226, 50, 5, 9, align: "right")
        .F("{Total:Amount}", 162, 226, 38, 5, 9, align: "right")
        .F("Estimated Tax", 110, 232, 50, 5, 9, align: "right")
        .F("{Values:TaxAmount}", 162, 232, 38, 5, 9, align: "right")
        .F("QUOTE TOTAL", 110, 239, 50, 7, 11, bold: true, align: "right")
        .F("{Values:TotalAmount}", 162, 239, 38, 7, 11, bold: true, align: "right")
        .F("Terms and conditions: {Values:Terms}", 10, 226, 95, 20, 9)
        .F("Accepted by: ______________________   Date: ____________", 10, 254, 150, 6, 9)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition UsPackingSlip() => new FormTemplateBuilder("t_us_packing_slip", "Packing Slip (US)", FormCategories.Delivery, region: FormRegions.UnitedStates)
        .H("PACKING SLIP", 10, 12, 120, 10, 18, bold: true)
        .H("{Values:CompanyName}", 10, 24, 110, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 110, 5, 9)
        .H("Order No.", 130, 12, 30, 5, 9, align: "right")
        .H("{Values:OrderNo}", 162, 12, 38, 5, 9, align: "right")
        .H("Ship Date", 130, 18, 30, 5, 9, align: "right")
        .H("{Date}", 162, 18, 38, 5, 9, align: "right")
        .H("Carrier", 130, 24, 30, 5, 9, align: "right")
        .H("{Values:Carrier}", 162, 24, 38, 5, 9, align: "right")
        .H("Tracking No.", 130, 30, 30, 5, 9, align: "right")
        .H("{Values:TrackingNo}", 162, 30, 38, 5, 9, align: "right")
        .Rule(40)
        .H("BILL TO", 10, 44, 90, 5, 9, bold: true)
        .H("SHIP TO", 105, 44, 95, 5, 9, bold: true)
        .H("{Values:BillToName}", 10, 50, 90, 5, 10)
        .H("{Values:BillToAddress}", 10, 56, 90, 10, 9)
        .H("{Values:ShipToName}", 105, 50, 95, 5, 10)
        .H("{Values:ShipToAddress}", 105, 56, 95, 10, 9)
        .Table(10, 74, 190, 7,
            new FormTemplateColumn("Item No.", 30),
            new FormTemplateColumn("Description", 74),
            new FormTemplateColumn("Qty Ordered", 28, "right"),
            new FormTemplateColumn("Qty Shipped", 28, "right"),
            new FormTemplateColumn("Back Order", 30, "right"))
        .F("Total cartons: {Values:Cartons}    Total weight: {Values:Weight}", 10, 228, 190, 6, 9)
        .F("Notes: {Values:Notes}", 10, 236, 190, 10, 9)
        .F("Received by (print name): ______________________   Signature: ________________   Date: __________", 10, 256, 190, 6, 9)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition UsPayStub() => new FormTemplateBuilder("t_us_pay_stub", "Pay Stub (US)", FormCategories.Payroll, region: FormRegions.UnitedStates)
        .H("PAY STATEMENT", 10, 12, 130, 10, 18, bold: true)
        .H("{Values:CompanyName}", 10, 24, 110, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 110, 5, 9)
        .H("Pay Date", 140, 12, 30, 5, 9, align: "right")
        .H("{Values:PayDate}", 172, 12, 28, 5, 9, align: "right")
        .H("Pay Period", 140, 18, 30, 5, 9, align: "right")
        .H("{Values:PayPeriod}", 172, 18, 28, 5, 9, align: "right")
        .H("Employee ID", 140, 24, 30, 5, 9, align: "right")
        .H("{Values:EmployeeNo}", 172, 24, 28, 5, 9, align: "right")
        .Rule(40)
        .H("Employee: {Values:EmployeeName}", 10, 44, 100, 6, 11, bold: true)
        .H("SSN: XXX-XX-{Values:SsnLast4}    Department: {Values:Department}", 10, 51, 140, 5, 9)
        .Table(10, 62, 190, 7,
            new FormTemplateColumn("Earnings", 90),
            new FormTemplateColumn("Hours", 24, "right"),
            new FormTemplateColumn("Rate", 32, "right"),
            new FormTemplateColumn("Amount", 44, "right"))
        .F("GROSS PAY", 10, 210, 80, 6, 10, bold: true)
        .F("{Values:GrossPay}", 92, 210, 40, 6, 10, bold: true, align: "right")
        .F("Federal Income Tax", 10, 218, 80, 5, 9)
        .F("{Values:FederalTax}", 92, 218, 40, 5, 9, align: "right")
        .F("Social Security (6.2%)", 10, 224, 80, 5, 9)
        .F("{Values:SocialSecurity}", 92, 224, 40, 5, 9, align: "right")
        .F("Medicare (1.45%)", 10, 230, 80, 5, 9)
        .F("{Values:Medicare}", 92, 230, 40, 5, 9, align: "right")
        .F("State Income Tax", 10, 236, 80, 5, 9)
        .F("{Values:StateTax}", 92, 236, 40, 5, 9, align: "right")
        .F("Other deductions", 10, 242, 80, 5, 9)
        .F("{Values:OtherDeductions}", 92, 242, 40, 5, 9, align: "right")
        .F("NET PAY", 10, 251, 80, 8, 12, bold: true)
        .F("{Values:NetPay}", 92, 251, 40, 8, 12, bold: true, align: "right")
        .F("YTD GROSS", 10, 262, 80, 5, 9, bold: true)
        .F("{Values:YtdGross}", 92, 262, 40, 5, 9, align: "right")
        .F("YTD NET", 10, 268, 80, 5, 9, bold: true)
        .F("{Values:YtdNet}", 92, 268, 40, 5, 9, align: "right")
        .F("PTO balance: {Values:PtoBalance} hours", 140, 218, 60, 5, 9)
        .F("Pay method: Direct deposit", 140, 224, 60, 5, 9)
        .F("This statement is for informational purposes only.", 10, 278, 190, 5, 8)
        .F("Page {Page} of {Pages}", 150, 284, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition UsStatement() => new FormTemplateBuilder("t_us_statement", "Statement of Account (US)", FormCategories.Statement, region: FormRegions.UnitedStates)
        .H("STATEMENT OF ACCOUNT", 10, 12, 120, 10, 18, bold: true)
        .H("{Values:CompanyName}", 10, 24, 110, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 110, 5, 9)
        .H("Statement Date", 140, 12, 30, 5, 9, align: "right")
        .H("{Date}", 172, 12, 28, 5, 9, align: "right")
        .H("Account No.", 140, 18, 30, 5, 9, align: "right")
        .H("{Values:AccountNo}", 172, 18, 28, 5, 9, align: "right")
        .Rule(40)
        .H("CUSTOMER", 10, 44, 120, 5, 9, bold: true)
        .H("{Values:CustomerName}", 10, 50, 120, 5, 10)
        .H("{Values:CustomerAddress}", 10, 56, 120, 10, 9)
        .H("Previous Balance", 10, 74, 60, 5, 9)
        .H("{Values:PreviousBalance}", 70, 74, 40, 5, 9, align: "right")
        .H("Payments / Credits", 10, 81, 60, 5, 9)
        .H("{Values:Payments}", 70, 81, 40, 5, 9, align: "right")
        .H("New Charges", 10, 88, 60, 5, 9)
        .H("{Total:Charges}", 70, 88, 40, 5, 9, align: "right")
        .H("AMOUNT DUE", 10, 97, 70, 7, 11, bold: true)
        .H("{Values:AmountDue}", 80, 97, 40, 7, 11, bold: true, align: "right")
        .Table(10, 112, 190, 7,
            new FormTemplateColumn("Date", 22, "center"),
            new FormTemplateColumn("Document", 34),
            new FormTemplateColumn("Description", 62),
            new FormTemplateColumn("Charges", 24, "right"),
            new FormTemplateColumn("Credits", 24, "right"),
            new FormTemplateColumn("Balance", 24, "right"))
        .F("Aging:  Current {Values:AgingCurrent}   1-30 {Values:Aging30}   31-60 {Values:Aging60}   61-90 {Values:Aging90}   Over 90 {Values:AgingOver90}", 10, 226, 190, 6, 9)
        .F("Please remit payment to: {Values:PaymentDetails}", 10, 236, 190, 8, 9)
        .F("Questions about this statement? {Values:CompanyPhone} / {Values:CompanyEmail}", 10, 250, 190, 5, 9)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    // ── 英国 ──

    private static FormDefinition UkVatInvoice() => new FormTemplateBuilder("t_uk_vat_invoice", "VAT Invoice (UK)", FormCategories.Invoice, region: FormRegions.UnitedKingdom)
        .H("VAT INVOICE", 10, 12, 120, 10, 20, bold: true)
        .H("{Values:CompanyName}", 10, 24, 110, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 110, 5, 9)
        .H("VAT Registration No. {Values:VatNumber}", 10, 36, 110, 5, 9)
        .H("Invoice No.", 130, 12, 30, 5, 9, align: "right")
        .H("{Values:InvoiceNo}", 162, 12, 38, 5, 9, align: "right")
        .H("Invoice Date", 130, 18, 30, 5, 9, align: "right")
        .H("{Date}", 162, 18, 38, 5, 9, align: "right")
        .H("Supply Date", 130, 24, 30, 5, 9, align: "right")
        .H("{Values:SupplyDate}", 162, 24, 38, 5, 9, align: "right")
        .H("PO Number", 130, 30, 30, 5, 9, align: "right")
        .H("{Values:PoNumber}", 162, 30, 38, 5, 9, align: "right")
        .Rule(44)
        .H("CUSTOMER", 10, 48, 100, 5, 9, bold: true)
        .H("{Values:CustomerName}", 10, 54, 100, 5, 10)
        .H("{Values:CustomerAddress}", 10, 60, 100, 10, 9)
        .H("Customer VAT No. {Values:CustomerVatNumber}", 10, 72, 100, 5, 9)
        .Table(10, 84, 190, 7,
            new FormTemplateColumn("Description", 70),
            new FormTemplateColumn("Qty", 14, "right"),
            new FormTemplateColumn("Unit Price (excl. VAT)", 34, "right"),
            new FormTemplateColumn("VAT Rate", 18, "right"),
            new FormTemplateColumn("Net", 28, "right"),
            new FormTemplateColumn("VAT", 26, "right"))
        .F("Total Net", 110, 222, 50, 5, 9, align: "right")
        .F("{Total:Net}", 162, 222, 38, 5, 9, align: "right")
        .F("Total VAT", 110, 228, 50, 5, 9, align: "right")
        .F("{Values:TotalVat}", 162, 228, 38, 5, 9, align: "right")
        .F("TOTAL (incl. VAT)", 110, 235, 50, 7, 11, bold: true, align: "right")
        .F("{Values:TotalAmount}", 162, 235, 38, 7, 11, bold: true, align: "right")
        .F("VAT breakdown", 10, 222, 90, 5, 9, bold: true)
        .F("20%: {Values:Vat20}    5%: {Values:Vat5}    0%: {Values:Vat0}", 10, 228, 95, 5, 9)
        .F("Payment due by {Values:DueDate}. Please pay by bank transfer.", 10, 238, 95, 5, 9)
        .F("{Values:BankDetails}", 10, 244, 95, 10, 9)
        .F("Reverse charge: {Values:ReverseCharge}", 10, 256, 190, 5, 8)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition UkCreditNote() => new FormTemplateBuilder("t_uk_credit_note", "Credit Note (UK)", FormCategories.Credit, region: FormRegions.UnitedKingdom)
        .H("CREDIT NOTE", 10, 12, 120, 10, 20, bold: true)
        .H("{Values:CompanyName}", 10, 24, 110, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 110, 5, 9)
        .H("VAT Registration No. {Values:VatNumber}", 10, 36, 110, 5, 9)
        .H("Credit Note No.", 130, 12, 30, 5, 9, align: "right")
        .H("{Values:CreditNoteNo}", 162, 12, 38, 5, 9, align: "right")
        .H("Date", 130, 18, 30, 5, 9, align: "right")
        .H("{Date}", 162, 18, 38, 5, 9, align: "right")
        .H("Original Invoice", 130, 24, 30, 5, 9, align: "right")
        .H("{Values:OriginalInvoiceNo}", 162, 24, 38, 5, 9, align: "right")
        .Rule(44)
        .H("CUSTOMER", 10, 48, 100, 5, 9, bold: true)
        .H("{Values:CustomerName}", 10, 54, 100, 5, 10)
        .H("{Values:CustomerAddress}", 10, 60, 100, 10, 9)
        .Table(10, 84, 190, 7,
            new FormTemplateColumn("Description", 86),
            new FormTemplateColumn("Net", 32, "right"),
            new FormTemplateColumn("VAT Rate", 20, "right"),
            new FormTemplateColumn("VAT", 26, "right"),
            new FormTemplateColumn("Total credit", 26, "right"))
        .F("Reason for credit: {Values:Reason}", 10, 210, 190, 8, 9)
        .F("Total Net", 110, 224, 50, 5, 9, align: "right")
        .F("{Total:Net}", 162, 224, 38, 5, 9, align: "right")
        .F("Total VAT", 110, 230, 50, 5, 9, align: "right")
        .F("{Values:TotalVat}", 162, 230, 38, 5, 9, align: "right")
        .F("TOTAL CREDIT", 110, 237, 50, 7, 11, bold: true, align: "right")
        .F("{Values:TotalAmount}", 162, 237, 38, 7, 11, bold: true, align: "right")
        .F("This credit note corrects the invoice referenced above.", 10, 250, 190, 5, 9)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    // ── EU ──

    private static FormDefinition EuVatInvoice() => new FormTemplateBuilder("t_eu_vat_invoice", "VAT Invoice (EU)", FormCategories.Invoice, region: FormRegions.Europe)
        .H("VAT INVOICE", 10, 12, 120, 10, 20, bold: true)
        .H("{Values:CompanyName}", 10, 24, 100, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 100, 5, 9)
        .H("Supplier VAT ID: {Values:VatNumber}", 10, 36, 100, 5, 9)
        .H("Invoice No.", 130, 12, 30, 5, 9, align: "right")
        .H("{Values:InvoiceNo}", 162, 12, 38, 5, 9, align: "right")
        .H("Invoice Date", 130, 18, 30, 5, 9, align: "right")
        .H("{Date}", 162, 18, 38, 5, 9, align: "right")
        .H("Date of Supply", 130, 24, 30, 5, 9, align: "right")
        .H("{Values:SupplyDate}", 162, 24, 38, 5, 9, align: "right")
        .H("Order / PO No.", 130, 30, 30, 5, 9, align: "right")
        .H("{Values:PoNumber}", 162, 30, 38, 5, 9, align: "right")
        .Rule(44)
        .H("CUSTOMER", 10, 48, 100, 5, 9, bold: true)
        .H("{Values:CustomerName}", 10, 54, 100, 5, 10)
        .H("{Values:CustomerAddress}", 10, 60, 100, 10, 9)
        .H("Customer VAT ID: {Values:CustomerVatNumber}", 10, 72, 100, 5, 9)
        .Table(10, 84, 190, 7,
            new FormTemplateColumn("Description", 70),
            new FormTemplateColumn("Qty", 14, "right"),
            new FormTemplateColumn("Unit Price (excl. VAT)", 34, "right"),
            new FormTemplateColumn("VAT Rate", 18, "right"),
            new FormTemplateColumn("Net Amount", 28, "right"),
            new FormTemplateColumn("VAT Amount", 26, "right"))
        .F("Total Net", 110, 218, 50, 5, 9, align: "right")
        .F("{Total:Net Amount}", 162, 218, 38, 5, 9, align: "right")
        .F("Total VAT", 110, 224, 50, 5, 9, align: "right")
        .F("{Values:TotalVat}", 162, 224, 38, 5, 9, align: "right")
        .F("TOTAL DUE", 110, 231, 50, 7, 11, bold: true, align: "right")
        .F("{Values:TotalAmount}", 162, 231, 38, 7, 11, bold: true, align: "right")
        .F("VAT breakdown by rate", 10, 218, 90, 5, 9, bold: true)
        .F("Standard rate: {Values:VatStandard}    Reduced rate: {Values:VatReduced}    Zero rated: {Values:VatZero}", 10, 224, 95, 8, 9)
        .F("Payment terms: {Values:Terms}", 10, 236, 95, 5, 9)
        .F("{Values:BankDetails}", 10, 242, 95, 10, 9)
        .F("Reverse charge: {Values:ReverseCharge}    Exemption: {Values:Exemption}", 10, 256, 190, 5, 8)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition EuProformaInvoice() => new FormTemplateBuilder("t_eu_proforma", "Proforma Invoice (EU)", FormCategories.Invoice, region: FormRegions.Europe)
        .H("PROFORMA INVOICE", 10, 12, 130, 10, 18, bold: true)
        .H("This is not a VAT invoice.", 10, 23, 130, 5, 8)
        .H("{Values:CompanyName}", 10, 30, 100, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 36, 100, 5, 9)
        .H("Proforma No.", 130, 12, 30, 5, 9, align: "right")
        .H("{Values:InvoiceNo}", 162, 12, 38, 5, 9, align: "right")
        .H("Date", 130, 18, 30, 5, 9, align: "right")
        .H("{Date}", 162, 18, 38, 5, 9, align: "right")
        .H("Valid Until", 130, 24, 30, 5, 9, align: "right")
        .H("{Values:ValidUntil}", 162, 24, 38, 5, 9, align: "right")
        .H("Incoterms", 130, 30, 30, 5, 9, align: "right")
        .H("{Values:Incoterms}", 162, 30, 38, 5, 9, align: "right")
        .Rule(46)
        .H("CONSIGNEE", 10, 50, 100, 5, 9, bold: true)
        .H("{Values:CustomerName}", 10, 56, 100, 5, 10)
        .H("{Values:CustomerAddress}", 10, 62, 100, 10, 9)
        .Table(10, 78, 190, 7,
            new FormTemplateColumn("Description", 78),
            new FormTemplateColumn("HS Code", 24),
            new FormTemplateColumn("Qty", 16, "right"),
            new FormTemplateColumn("Unit Value", 34, "right"),
            new FormTemplateColumn("Amount", 38, "right"))
        .F("Goods value", 110, 222, 50, 5, 9, align: "right")
        .F("{Total:Amount}", 162, 222, 38, 5, 9, align: "right")
        .F("Freight", 110, 228, 50, 5, 9, align: "right")
        .F("{Values:Freight}", 162, 228, 38, 5, 9, align: "right")
        .F("Insurance", 110, 234, 50, 5, 9, align: "right")
        .F("{Values:Insurance}", 162, 234, 38, 5, 9, align: "right")
        .F("TOTAL (excl. VAT)", 110, 241, 50, 7, 11, bold: true, align: "right")
        .F("{Values:TotalAmount}", 162, 241, 38, 7, 11, bold: true, align: "right")
        .F("Country of origin: {Values:OriginCountry}", 10, 222, 95, 5, 9)
        .F("Payment terms: {Values:Terms}", 10, 228, 95, 5, 9)
        .F("{Values:BankDetails}", 10, 234, 95, 12, 9)
        .F("Customs / export notes: {Values:Notes}", 10, 250, 190, 10, 8)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition EuCreditNote() => new FormTemplateBuilder("t_eu_credit_note", "Credit Note (EU)", FormCategories.Credit, region: FormRegions.Europe)
        .H("CREDIT NOTE", 10, 12, 120, 10, 20, bold: true)
        .H("{Values:CompanyName}", 10, 24, 100, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 100, 5, 9)
        .H("Supplier VAT ID: {Values:VatNumber}", 10, 36, 100, 5, 9)
        .H("Credit Note No.", 130, 12, 30, 5, 9, align: "right")
        .H("{Values:CreditNoteNo}", 162, 12, 38, 5, 9, align: "right")
        .H("Date", 130, 18, 30, 5, 9, align: "right")
        .H("{Date}", 162, 18, 38, 5, 9, align: "right")
        .H("Original Invoice", 130, 24, 30, 5, 9, align: "right")
        .H("{Values:OriginalInvoiceNo}", 162, 24, 38, 5, 9, align: "right")
        .H("Original Date", 130, 30, 30, 5, 9, align: "right")
        .H("{Values:OriginalInvoiceDate}", 162, 30, 38, 5, 9, align: "right")
        .Rule(46)
        .H("CUSTOMER", 10, 50, 100, 5, 9, bold: true)
        .H("{Values:CustomerName}", 10, 56, 100, 5, 10)
        .H("{Values:CustomerAddress}", 10, 62, 100, 10, 9)
        .H("Customer VAT ID: {Values:CustomerVatNumber}", 10, 74, 100, 5, 9)
        .Table(10, 86, 190, 7,
            new FormTemplateColumn("Description", 78),
            new FormTemplateColumn("Qty", 14, "right"),
            new FormTemplateColumn("Net Amount", 32, "right"),
            new FormTemplateColumn("VAT Rate", 18, "right"),
            new FormTemplateColumn("VAT Amount", 24, "right"),
            new FormTemplateColumn("Total", 24, "right"))
        .F("Reason for credit: {Values:Reason}", 10, 208, 190, 8, 9)
        .F("Total Net", 110, 222, 50, 5, 9, align: "right")
        .F("{Total:Net Amount}", 162, 222, 38, 5, 9, align: "right")
        .F("Total VAT", 110, 228, 50, 5, 9, align: "right")
        .F("{Values:TotalVat}", 162, 228, 38, 5, 9, align: "right")
        .F("TOTAL CREDIT", 110, 235, 50, 7, 11, bold: true, align: "right")
        .F("{Values:TotalAmount}", 162, 235, 38, 7, 11, bold: true, align: "right")
        .F("This document corrects the invoice referenced above (EU VAT Directive, Article 226).", 10, 248, 190, 5, 8)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    // ── オーストラリア ──

    private static FormDefinition AuTaxInvoice() => new FormTemplateBuilder("t_au_tax_invoice", "Tax Invoice (Australia)", FormCategories.Invoice, region: FormRegions.Australia)
        .H("TAX INVOICE", 10, 12, 120, 10, 20, bold: true)
        .H("{Values:CompanyName}", 10, 24, 100, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 100, 5, 9)
        .H("ABN: {Values:Abn}", 10, 36, 100, 5, 9)
        .H("Invoice No.", 130, 12, 30, 5, 9, align: "right")
        .H("{Values:InvoiceNo}", 162, 12, 38, 5, 9, align: "right")
        .H("Invoice Date", 130, 18, 30, 5, 9, align: "right")
        .H("{Date}", 162, 18, 38, 5, 9, align: "right")
        .H("Due Date", 130, 24, 30, 5, 9, align: "right")
        .H("{Values:DueDate}", 162, 24, 38, 5, 9, align: "right")
        .H("PO Number", 130, 30, 30, 5, 9, align: "right")
        .H("{Values:PoNumber}", 162, 30, 38, 5, 9, align: "right")
        .Rule(44)
        .H("CUSTOMER", 10, 48, 100, 5, 9, bold: true)
        .H("{Values:CustomerName}", 10, 54, 100, 5, 10)
        .H("{Values:CustomerAddress}", 10, 60, 100, 10, 9)
        .H("Customer ABN: {Values:CustomerAbn}", 10, 72, 100, 5, 9)
        .Table(10, 84, 190, 7,
            new FormTemplateColumn("Description", 84),
            new FormTemplateColumn("Qty", 16, "right"),
            new FormTemplateColumn("Unit Price (excl. GST)", 34, "right"),
            new FormTemplateColumn("GST 10%", 26, "right"),
            new FormTemplateColumn("Total (incl. GST)", 30, "right"))
        .F("Subtotal (excl. GST)", 110, 220, 50, 5, 9, align: "right")
        .F("{Values:SubtotalExGst}", 162, 220, 38, 5, 9, align: "right")
        .F("GST 10%", 110, 226, 50, 5, 9, align: "right")
        .F("{Values:GstAmount}", 162, 226, 38, 5, 9, align: "right")
        .F("TOTAL (incl. GST)", 110, 233, 50, 7, 11, bold: true, align: "right")
        .F("{Values:TotalAmount}", 162, 233, 38, 7, 11, bold: true, align: "right")
        .F("Total price includes GST.", 10, 220, 95, 5, 9, bold: true)
        .F("Payment details: {Values:PaymentDetails}", 10, 228, 95, 10, 9)
        .F("BSB: {Values:Bsb}   Account: {Values:AccountNumber}", 10, 240, 95, 5, 9)
        .F("Terms: {Values:Terms}", 10, 246, 95, 5, 9)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition AuQuote() => new FormTemplateBuilder("t_au_quote", "Quote (Australia)", FormCategories.Estimate, region: FormRegions.Australia)
        .H("QUOTE", 10, 12, 120, 10, 20, bold: true)
        .H("{Values:CompanyName}", 10, 24, 100, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 100, 5, 9)
        .H("ABN: {Values:Abn}", 10, 36, 100, 5, 9)
        .H("Quote No.", 130, 12, 30, 5, 9, align: "right")
        .H("{Values:QuoteNo}", 162, 12, 38, 5, 9, align: "right")
        .H("Date", 130, 18, 30, 5, 9, align: "right")
        .H("{Date}", 162, 18, 38, 5, 9, align: "right")
        .H("Valid Until", 130, 24, 30, 5, 9, align: "right")
        .H("{Values:ValidUntil}", 162, 24, 38, 5, 9, align: "right")
        .Rule(44)
        .H("CLIENT", 10, 48, 100, 5, 9, bold: true)
        .H("{Values:CustomerName}", 10, 54, 100, 5, 10)
        .H("{Values:CustomerAddress}", 10, 60, 100, 10, 9)
        .H("Site / Project: {Values:ProjectName}", 10, 72, 190, 5, 9)
        .Table(10, 84, 190, 7,
            new FormTemplateColumn("Description", 100),
            new FormTemplateColumn("Qty", 18, "right"),
            new FormTemplateColumn("Rate (excl. GST)", 34, "right"),
            new FormTemplateColumn("Amount (excl. GST)", 38, "right"))
        .F("Subtotal (excl. GST)", 110, 222, 60, 5, 9, align: "right")
        .F("{Total:Amount (excl. GST)}", 172, 222, 28, 5, 9, align: "right")
        .F("GST 10%", 110, 228, 60, 5, 9, align: "right")
        .F("{Values:GstAmount}", 172, 228, 28, 5, 9, align: "right")
        .F("TOTAL (incl. GST)", 110, 235, 60, 7, 11, bold: true, align: "right")
        .F("{Values:TotalAmount}", 172, 235, 28, 7, 11, bold: true, align: "right")
        .F("Inclusions / exclusions: {Values:Notes}", 10, 222, 95, 16, 9)
        .F("Acceptance: ______________________   Date: ____________", 10, 252, 150, 6, 9)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition AuPayslip() => new FormTemplateBuilder("t_au_payslip", "Payslip (Australia)", FormCategories.Payroll, region: FormRegions.Australia)
        .H("PAYSLIP", 10, 12, 120, 10, 18, bold: true)
        .H("{Values:CompanyName}", 10, 24, 110, 5, 10, bold: true)
        .H("ABN: {Values:Abn}", 10, 30, 110, 5, 9)
        .H("Pay Date", 140, 12, 30, 5, 9, align: "right")
        .H("{Values:PayDate}", 172, 12, 28, 5, 9, align: "right")
        .H("Pay Period", 140, 18, 30, 5, 9, align: "right")
        .H("{Values:PayPeriod}", 172, 18, 28, 5, 9, align: "right")
        .H("Pay Cycle", 140, 24, 30, 5, 9, align: "right")
        .H("{Values:PayCycle}", 172, 24, 28, 5, 9, align: "right")
        .Rule(40)
        .H("Employee: {Values:EmployeeName}", 10, 44, 110, 6, 11, bold: true)
        .H("Employee ID: {Values:EmployeeNo}    Classification: {Values:Classification}", 10, 51, 150, 5, 9)
        .H("Hourly rate: {Values:HourlyRate}    Annual salary: {Values:AnnualSalary}", 10, 57, 150, 5, 9)
        .Table(10, 68, 190, 7,
            new FormTemplateColumn("Description", 88),
            new FormTemplateColumn("Hours", 22, "right"),
            new FormTemplateColumn("Rate", 32, "right"),
            new FormTemplateColumn("Amount", 48, "right"))
        .F("Gross Pay", 10, 212, 70, 6, 10, bold: true)
        .F("{Values:GrossPay}", 82, 212, 40, 6, 10, bold: true, align: "right")
        .F("Tax withheld", 10, 220, 70, 5, 9)
        .F("{Values:TaxWithheld}", 82, 220, 40, 5, 9, align: "right")
        .F("Superannuation ({Values:SuperRate})", 10, 226, 70, 5, 9)
        .F("{Values:SuperAmount}", 82, 226, 40, 5, 9, align: "right")
        .F("Allowances", 10, 232, 70, 5, 9)
        .F("{Values:Allowances}", 82, 232, 40, 5, 9, align: "right")
        .F("Deductions", 10, 238, 70, 5, 9)
        .F("{Values:Deductions}", 82, 238, 40, 5, 9, align: "right")
        .F("NET PAY", 10, 247, 70, 8, 12, bold: true)
        .F("{Values:NetPay}", 82, 247, 40, 8, 12, bold: true, align: "right")
        .F("YTD gross: {Values:YtdGross}    YTD tax: {Values:YtdTax}    YTD super: {Values:YtdSuper}", 10, 262, 190, 5, 9)
        .F("Leave balance: annual {Values:AnnualLeave} hrs, personal {Values:PersonalLeave} hrs", 10, 268, 190, 5, 9)
        .F("Superannuation is paid to {Values:SuperFund}. Please keep this payslip for your records.", 10, 278, 190, 5, 8)
        .F("Page {Page} of {Pages}", 150, 284, 50, 5, 8, align: "right")
        .Build();

    // ── ニュージーランド ──

    private static FormDefinition NzTaxInvoice() => new FormTemplateBuilder("t_nz_tax_invoice", "Tax Invoice (New Zealand)", FormCategories.Invoice, region: FormRegions.NewZealand)
        .H("TAX INVOICE", 10, 12, 120, 10, 20, bold: true)
        .H("{Values:CompanyName}", 10, 24, 100, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 100, 5, 9)
        .H("GST No: {Values:GstNumber}    NZBN: {Values:Nzbn}", 10, 36, 110, 5, 9)
        .H("Invoice No.", 130, 12, 30, 5, 9, align: "right")
        .H("{Values:InvoiceNo}", 162, 12, 38, 5, 9, align: "right")
        .H("Invoice Date", 130, 18, 30, 5, 9, align: "right")
        .H("{Date}", 162, 18, 38, 5, 9, align: "right")
        .H("Due Date", 130, 24, 30, 5, 9, align: "right")
        .H("{Values:DueDate}", 162, 24, 38, 5, 9, align: "right")
        .Rule(44)
        .H("CUSTOMER", 10, 48, 100, 5, 9, bold: true)
        .H("{Values:CustomerName}", 10, 54, 100, 5, 10)
        .H("{Values:CustomerAddress}", 10, 60, 100, 10, 9)
        .Table(10, 80, 190, 7,
            new FormTemplateColumn("Description", 92),
            new FormTemplateColumn("Qty", 16, "right"),
            new FormTemplateColumn("Unit Price", 34, "right"),
            new FormTemplateColumn("GST 15%", 22, "right"),
            new FormTemplateColumn("Total", 26, "right"))
        .F("Subtotal (excl. GST)", 110, 222, 50, 5, 9, align: "right")
        .F("{Values:SubtotalExGst}", 162, 222, 38, 5, 9, align: "right")
        .F("GST 15%", 110, 228, 50, 5, 9, align: "right")
        .F("{Values:GstAmount}", 162, 228, 38, 5, 9, align: "right")
        .F("TOTAL (incl. GST)", 110, 235, 50, 7, 11, bold: true, align: "right")
        .F("{Values:TotalAmount}", 162, 235, 38, 7, 11, bold: true, align: "right")
        .F("Payment details: {Values:PaymentDetails}", 10, 222, 95, 10, 9)
        .F("Account: {Values:AccountNumber}", 10, 234, 95, 5, 9)
        .F("Terms: {Values:Terms}", 10, 240, 95, 5, 9)
        .F("Total price includes GST.", 10, 252, 190, 5, 8, bold: true)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();

    private static FormDefinition NzPayslip() => new FormTemplateBuilder("t_nz_payslip", "Payslip (New Zealand)", FormCategories.Payroll, region: FormRegions.NewZealand)
        .H("PAYSLIP", 10, 12, 120, 10, 18, bold: true)
        .H("{Values:CompanyName}", 10, 24, 110, 5, 10, bold: true)
        .H("NZBN: {Values:Nzbn}", 10, 30, 110, 5, 9)
        .H("Pay Date", 140, 12, 30, 5, 9, align: "right")
        .H("{Values:PayDate}", 172, 12, 28, 5, 9, align: "right")
        .H("Pay Period", 140, 18, 30, 5, 9, align: "right")
        .H("{Values:PayPeriod}", 172, 18, 28, 5, 9, align: "right")
        .H("Employment type", 140, 24, 30, 5, 9, align: "right")
        .H("{Values:EmploymentType}", 172, 24, 28, 5, 9, align: "right")
        .Rule(40)
        .H("Employee: {Values:EmployeeName}", 10, 44, 110, 6, 11, bold: true)
        .H("Employee ID: {Values:EmployeeNo}    IRD number: {Values:IrdNumber}", 10, 51, 150, 5, 9)
        .H("Ordinary hourly rate: {Values:HourlyRate}    Tax code: {Values:TaxCode}", 10, 57, 150, 5, 9)
        .Table(10, 68, 190, 7,
            new FormTemplateColumn("Description", 92),
            new FormTemplateColumn("Hours", 22, "right"),
            new FormTemplateColumn("Rate", 34, "right"),
            new FormTemplateColumn("Amount", 42, "right"))
        .F("Gross Pay", 10, 212, 70, 6, 10, bold: true)
        .F("{Values:GrossPay}", 82, 212, 40, 6, 10, bold: true, align: "right")
        .F("PAYE", 10, 220, 70, 5, 9)
        .F("{Values:Paye}", 82, 220, 40, 5, 9, align: "right")
        .F("ACC levy", 10, 226, 70, 5, 9)
        .F("{Values:AccLevy}", 82, 226, 40, 5, 9, align: "right")
        .F("Kiwisaver (employee)", 10, 232, 70, 5, 9)
        .F("{Values:Kiwisaver}", 82, 232, 40, 5, 9, align: "right")
        .F("Other deductions", 10, 238, 70, 5, 9)
        .F("{Values:OtherDeductions}", 82, 238, 40, 5, 9, align: "right")
        .F("NET PAY", 10, 247, 70, 8, 12, bold: true)
        .F("{Values:NetPay}", 82, 247, 40, 8, 12, bold: true, align: "right")
        .F("Employer Kiwisaver contribution: {Values:EmployerKiwisaver}", 10, 262, 190, 5, 9)
        .F("YTD gross: {Values:YtdGross}    YTD PAYE: {Values:YtdPaye}", 10, 268, 190, 5, 9)
        .F("Holiday pay balance: {Values:HolidayPayBalance}    Sick leave: {Values:SickLeave} hrs", 10, 274, 190, 5, 9)
        .F("Page {Page} of {Pages}", 150, 284, 50, 5, 8, align: "right")
        .Build();

    // ── カナダ ──

    private static FormDefinition CaInvoice() => new FormTemplateBuilder("t_ca_invoice", "Invoice (Canada)", FormCategories.Invoice, region: FormRegions.Canada)
        .H("INVOICE", 10, 12, 120, 10, 20, bold: true)
        .H("{Values:CompanyName}", 10, 24, 100, 5, 10, bold: true)
        .H("{Values:CompanyAddress}", 10, 30, 100, 5, 9)
        .H("GST/HST No: {Values:GstNumber}", 10, 36, 100, 5, 9)
        .H("Invoice No.", 130, 12, 30, 5, 9, align: "right")
        .H("{Values:InvoiceNo}", 162, 12, 38, 5, 9, align: "right")
        .H("Invoice Date", 130, 18, 30, 5, 9, align: "right")
        .H("{Date}", 162, 18, 38, 5, 9, align: "right")
        .H("Due Date", 130, 24, 30, 5, 9, align: "right")
        .H("{Values:DueDate}", 162, 24, 38, 5, 9, align: "right")
        .H("Terms", 130, 30, 30, 5, 9, align: "right")
        .H("{Values:Terms}", 162, 30, 38, 5, 9, align: "right")
        .Rule(44)
        .H("BILL TO", 10, 48, 100, 5, 9, bold: true)
        .H("{Values:CustomerName}", 10, 54, 100, 5, 10)
        .H("{Values:CustomerAddress}", 10, 60, 100, 10, 9)
        .Table(10, 82, 190, 7,
            new FormTemplateColumn("Description", 96),
            new FormTemplateColumn("Qty", 18, "right"),
            new FormTemplateColumn("Unit Price", 34, "right"),
            new FormTemplateColumn("Amount", 42, "right"))
        .F("Subtotal", 110, 224, 50, 5, 9, align: "right")
        .F("{Total:Amount}", 162, 224, 38, 5, 9, align: "right")
        .F("GST/HST ({Values:TaxRate})", 110, 230, 50, 5, 9, align: "right")
        .F("{Values:TaxAmount}", 162, 230, 38, 5, 9, align: "right")
        .F("TOTAL", 110, 237, 50, 7, 11, bold: true, align: "right")
        .F("{Values:TotalAmount}", 162, 237, 38, 7, 11, bold: true, align: "right")
        .F("Payment details: {Values:PaymentDetails}", 10, 224, 95, 10, 9)
        .F("Notes: {Values:Notes}", 10, 238, 95, 12, 9)
        .F("Thank you for your business.", 10, 256, 150, 5, 9)
        .F("Page {Page} of {Pages}", 150, 282, 50, 5, 8, align: "right")
        .Build();
}
