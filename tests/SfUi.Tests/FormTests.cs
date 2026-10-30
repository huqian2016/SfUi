using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>帳票（SVF 風フォーム）のテンプレート・差し込み・HTML/SVG/画像出力のテスト。</summary>
[Collection("Localization")]
public class FormTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public FormTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    [Fact]
    public void TemplateLibraryHasUniqueIdsAndNames()
    {
        var ids = FormTemplateLibrary.All.Select(f => f.Id).ToList();
        var names = FormTemplateLibrary.All.Select(f => f.Name).ToList();

        Assert.True(FormTemplateLibrary.All.Count >= 30, $"expected many templates, got {FormTemplateLibrary.All.Count}");
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(names.Count, names.Distinct(StringComparer.CurrentCulture).Count());
        Assert.All(FormTemplateLibrary.All, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Name));
            Assert.Contains(f.Category, FormCategories.All);
            Assert.Contains(f.Region, FormRegions.All);
            Assert.True(f.IsBuiltIn);
        });
    }

    [Fact]
    public void InternationalTemplatesCoverTheEnglishMarkets()
    {
        foreach (var region in new[]
        {
            FormRegions.UnitedStates, FormRegions.UnitedKingdom, FormRegions.Europe,
            FormRegions.Australia, FormRegions.NewZealand, FormRegions.Canada,
        })
        {
            Assert.NotEmpty(FormTemplateLibrary.ByRegion(region));
        }

        Assert.All(FormTemplateLibrary.International, f => Assert.NotEqual(FormRegions.Japan, f.Region));
        Assert.True(FormTemplateLibrary.ByRegion(null).Count >= FormTemplateLibrary.All.Count);
    }

    [Fact]
    public void InternationalTemplatesCarryStatutoryFields()
    {
        static string Text(FormDefinition form) =>
            string.Join("\n", form.Header.Select(e => e.Text).Concat(form.Footer.Select(e => e.Text))
                .Concat(form.Table?.Columns.Select(c => c.Label) ?? Array.Empty<string>()));

        var byId = FormTemplateLibrary.All.ToDictionary(f => f.Id, StringComparer.Ordinal);

        Assert.Contains("SALES TAX", Text(byId["t_us_invoice"]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VAT", Text(byId["t_uk_vat_invoice"]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VAT", Text(byId["t_eu_vat_invoice"]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ABN", Text(byId["t_au_tax_invoice"]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GST", Text(byId["t_nz_tax_invoice"]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GST", Text(byId["t_ca_invoice"]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Superannuation", Text(byId["t_au_payslip"]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PAYE", Text(byId["t_nz_payslip"]), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TemplateLibraryCoversTheRequestedDocumentTypes()
    {
        foreach (var category in new[]
        {
            FormCategories.Estimate, FormCategories.Order, FormCategories.Delivery, FormCategories.Invoice,
            FormCategories.Receipt, FormCategories.Payroll, FormCategories.Tax, FormCategories.Statement,
        })
        {
            Assert.NotEmpty(FormTemplateLibrary.ByCategory(category));
        }

        var names = string.Join("|", FormTemplateLibrary.All.Select(f => f.Name));
        Assert.Contains("見積書", names, StringComparison.Ordinal);
        Assert.Contains("注文書", names, StringComparison.Ordinal);
        Assert.Contains("請求書", names, StringComparison.Ordinal);
        Assert.Contains("領収書", names, StringComparison.Ordinal);
        Assert.Contains("給与明細", names, StringComparison.Ordinal);
        Assert.Contains("源泉徴収", names, StringComparison.Ordinal);
        Assert.Contains("納品書", names, StringComparison.Ordinal);
        Assert.Contains("支払調書", names, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateElementsStayInsideThePage()
    {
        foreach (var form in FormTemplateLibrary.All)
        {
            var (width, height) = FormLayout.PageSize(form);
            foreach (var element in form.Header.Concat(form.Footer))
            {
                Assert.True(element.X >= 0 && element.Y >= 0, $"{form.Name}: negative position");
                Assert.True(element.X + element.Width <= width + 1, $"{form.Name}: element ({element.Text}) exceeds width");
                Assert.True(element.Y + element.Height <= height + 1, $"{form.Name}: element ({element.Text}) exceeds height");
            }

            if (form.Table is { } table)
            {
                Assert.True(table.Y + table.RowHeightMm * 3 <= height, $"{form.Name}: table does not fit");
                Assert.True(table.X + table.Width <= width + 1, $"{form.Name}: table exceeds width");
            }
        }
    }

    [Fact]
    public void LayoutSplitsRowsIntoPages()
    {
        var form = FormTemplateLibrary.Find("t_general")!;
        var table = Table(60);
        var data = Data(table);

        var perPage = FormLayout.RowsPerPage(form);
        var pages = FormLayout.SplitRows(form, table);

        Assert.True(perPage > 5, $"expected a sane row count, got {perPage}");
        Assert.Equal(Math.Ceiling(60d / perPage), pages.Count);
        Assert.Equal(60, pages.Sum(p => p.Count));
        Assert.Equal(pages.Count, FormLayout.PageCount(form, table.RowCount));
        _ = data;
    }

    [Fact]
    public void FormDataBuildsTotalsAndCounts()
    {
        var data = Data(Table(3));

        var values = data.BuildValues(1, 2);

        Assert.Equal("1,200", values["Total:金額"]);
        Assert.Equal("3", values["Rows"]);
        Assert.Equal("1", values["Page"]);
        Assert.Equal("2", values["Pages"]);
        Assert.Equal("得意先", values["CustomerName"]);
    }

    [Fact]
    public void HtmlContainsPagesPlaceholdersAndDetailRows()
    {
        var form = FormTemplateLibrary.Find("t_estimate_manufacturing")!;
        var data = Data(Table(3));

        var html = FormHtmlWriter.Write(form, data);

        Assert.Contains("@page", html, StringComparison.Ordinal);
        Assert.Contains("御見積書", html, StringComparison.Ordinal);
        Assert.Contains("得意先", html, StringComparison.Ordinal);
        Assert.Contains("Page 1 / 1", html, StringComparison.Ordinal);
        Assert.Contains("品名", html, StringComparison.Ordinal);
        Assert.Contains("1,200", html, StringComparison.Ordinal);
        Assert.Contains("item-2", html, StringComparison.Ordinal);
        Assert.Equal(1, Count(html, "class=\"page\""));
    }

    [Fact]
    public void HtmlEscapesValues()
    {
        var form = FormTemplateLibrary.Find("t_general")!;
        var table = new ReportTable
        {
            Name = "Rows",
            Columns = new[] { "Name" },
            Rows = new IReadOnlyList<string?>[] { new[] { "<script>alert(1)</script>" } },
        };

        var html = FormHtmlWriter.Write(form, new FormData { Title = "<b>t</b>", Table = table });

        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;t&lt;/b&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SvgPagesAreValidAndContainTheText()
    {
        var form = FormTemplateLibrary.Find("t_receipt")!;
        var data = Data(Table(2));

        var pages = FormSvgWriter.WritePages(form, data);

        var svg = Assert.Single(pages);
        Assert.StartsWith("<?xml", svg, StringComparison.Ordinal);
        Assert.Contains("<svg", svg, StringComparison.Ordinal);
        Assert.Contains("領　収　書", svg, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 210 297\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ImagesRenderAtTheRequestedResolution()
    {
        var form = FormTemplateLibrary.Find("t_invoice_service")!;
        var page = FormDrawList.Build(form, Data(Table(4)))[0];

        var png = FormImageWriter.RenderPage(page, ReportImageFormat.Png, 150);
        var jpg = FormImageWriter.RenderPage(page, ReportImageFormat.Jpeg, 96);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, jpg.Take(2).ToArray());
        Assert.True(png.Length > 5000);
        Assert.Equal(210d, FormImageWriter.MmToPixel(210, 96) / (96d / 25.4), 3);
    }

    [Fact]
    public async Task OutputWriterWritesEveryRequestedFormat()
    {
        var form = FormTemplateLibrary.Find("t_general")!;
        var directory = Path.Combine(_sandbox, "exports");

        var result = await FormOutputWriter.WriteAsync(
            form,
            Data(Table(3)),
            ReportFormats.Html | ReportFormats.Svg | ReportFormats.Png,
            directory,
            "form");

        Assert.Equal(3, result.Files.Count);
        Assert.Contains(result.Files, f => f.EndsWith("form.html", StringComparison.Ordinal));
        Assert.Contains(result.Files, f => f.EndsWith("form.svg", StringComparison.Ordinal));
        Assert.Contains(result.Files, f => f.EndsWith("form.png", StringComparison.Ordinal));
        Assert.All(result.Files, f => Assert.True(File.Exists(f), f));
    }

    [Fact]
    public void StoreCopiesTemplatesAndRejectsBuiltInIds()
    {
        var store = new FormStore(_paths, _log);

        var copy = store.CreateFromTemplate("t_receipt", "領収書（自社）");

        Assert.False(copy.IsBuiltIn);
        Assert.NotEqual("t_receipt", copy.Id);
        Assert.Equal("領収書（自社）", copy.Name);
        Assert.NotNull(store.Find(copy.Id));
        Assert.NotNull(store.Find("t_receipt"));
        Assert.True(File.Exists(_paths.FormsFile));

        var reopened = new FormStore(_paths, _log);
        Assert.Single(reopened.Forms);
        Assert.True(reopened.Delete(copy.Id));
        Assert.Empty(new FormStore(_paths, _log).Forms);
    }

    [Fact]
    public void StoreCombinesBuiltInsAndUserForms()
    {
        var store = new FormStore(_paths, _log);
        store.CreateFromTemplate("t_order_manufacturing", "注文書（A社）");

        var all = store.AllForms;

        Assert.Equal(FormTemplateLibrary.All.Count + 1, all.Count);
        Assert.Contains(all, f => f.Name == "注文書（A社）");
    }

    private static FormData Data(ReportTable table) => new()
    {
        Title = "テスト帳票",
        Org = "dev@example.com",
        Table = table,
        Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CustomerName"] = "得意先",
            ["Subject"] = "件名",
        },
    };

    private static ReportTable Table(int rows) => new()
    {
        Name = "Rows",
        Columns = new[] { "品名", "数量", "金額" },
        Rows = Enumerable.Range(0, rows)
            .Select(i => (IReadOnlyList<string?>)new[] { "item-" + i, "1", "400" })
            .ToList(),
        NumericColumns = new HashSet<int> { 1, 2 },
    };

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
