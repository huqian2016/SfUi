using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>帳票（SVF 風）レイアウトのページ計算・HTML 生成・ストア・レンダラのテスト。</summary>
[Collection("Localization")]
public class ReportFormTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public ReportFormTests()
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
    public void RowsPerPageUsesPaperMarginAndRegions()
    {
        var layout = new ReportLayoutDefinition
        {
            PaperSize = ReportPapers.A4,
            Orientation = "portrait",
            MarginMm = 12,
            Header = new ReportLayoutRegion { HeightMm = 18 },
            Footer = new ReportLayoutRegion { HeightMm = 10 },
            Summary = new ReportLayoutRegion { HeightMm = 12 },
            Detail = new ReportLayoutRegion { RowHeightMm = 7 },
        };

        // 297 - 24 = 273 usable, minus 40 for regions and 8 slack = 225 -> 32 rows
        Assert.Equal(32, ReportFormLayout.RowsPerPage(layout));
        Assert.Equal(1, ReportFormLayout.PageCount(layout, 0));
        Assert.Equal(2, ReportFormLayout.PageCount(layout, 33));
        Assert.Equal(3, ReportFormLayout.PageCount(layout, 65));
    }

    [Fact]
    public void LandscapeSwapsPaperSize()
    {
        var (width, height) = ReportPapers.SizeMm(ReportPapers.A4, "landscape");

        Assert.Equal(297d, width);
        Assert.Equal(210d, height);
    }

    [Fact]
    public void SubstituteReplacesKnownFieldsOnly()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Title"] = "Accounts" };

        Assert.Equal("Accounts / {Org}", ReportFormLayout.Substitute("{Title} / {Org}", values));
    }

    [Fact]
    public void FormHtmlContainsHeaderDetailAndFooter()
    {
        var layout = new ReportLayoutDefinition
        {
            Name = "Invoice form",
            Header = new ReportLayoutRegion { Text = "{Title} for {Org}", HeightMm = 18 },
            Detail = new ReportLayoutRegion { HeightMm = 10, RowHeightMm = 7 },
            Summary = new ReportLayoutRegion { Text = UiText.T("Report_FormSummaryFmt"), Align = "right" },
            Footer = new ReportLayoutRegion { Text = "{Page} / {Pages}", Align = "center" },
        };

        var html = ReportFormWriter.Write(Document(75), layout);

        Assert.Contains("<html lang=\"", html, StringComparison.Ordinal);
        Assert.Contains("Accounts form for e2e@example.com", html, StringComparison.Ordinal);
        Assert.Contains("<table class=\"detail\">", html, StringComparison.Ordinal);
        Assert.Contains("<th>Name</th>", html, StringComparison.Ordinal);
        Assert.Contains("1 / 3", html, StringComparison.Ordinal);
        Assert.Contains("3 / 3", html, StringComparison.Ordinal);
        Assert.Contains("class=\"page\"", html, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(html, "<div class=\"page\">"));
    }

    [Fact]
    public void FormHtmlSplitsRowsAcrossPages()
    {
        var layout = new ReportLayoutDefinition
        {
            Header = new ReportLayoutRegion { Text = "{Title}", HeightMm = 18 },
            Footer = new ReportLayoutRegion { Text = "{Page}", HeightMm = 10 },
            Detail = new ReportLayoutRegion { RowHeightMm = 7 },
        };
        var perPage = ReportFormLayout.RowsPerPage(layout);

        var html = ReportFormWriter.Write(Document(perPage + 5), layout);

        Assert.Contains("row-" + (perPage + 4), html, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(html, "<div class=\"page\">"));
        Assert.Contains(">2<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void FormHtmlEscapesValuesAndShowsEmptyState()
    {
        var layout = new ReportLayoutDefinition();
        var document = new ReportDocument { Title = "<b>Danger</b>", Org = "org" };
        document.Sections.Add(ReportSection.FromTable(new ReportTable
        {
            Name = "Rows",
            Columns = new[] { "Name" },
            Rows = new[] { new[] { "<script>" } },
        }));

        var html = ReportFormWriter.Write(document, layout);
        Assert.Contains("&lt;b&gt;Danger&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);

        var empty = ReportFormWriter.Write(Document(0), layout);
        Assert.Contains(UiText.T("Report_FormNoRows"), empty, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutStoreRoundTripsAndKeepsADefault()
    {
        var store = new ReportLayoutStore(_paths, _log);
        var layout = ReportLayoutStore.CreateDefault("Standard invoice");

        var saved = store.Upsert(layout);

        Assert.False(string.IsNullOrWhiteSpace(saved.Id));
        Assert.True(File.Exists(_paths.ReportLayoutsFile));
        Assert.EndsWith(Path.Combine("reports", "layouts.json"), store.FilePath, StringComparison.OrdinalIgnoreCase);

        var reopened = new ReportLayoutStore(_paths, _log);
        var loaded = reopened.Find(saved.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Standard invoice", loaded!.Name);
        Assert.Equal(saved.Id, reopened.DefaultLayoutId);
        Assert.Equal(saved.Id, reopened.FindDefault()?.Id);

        loaded.Name = "Renamed";
        reopened.Upsert(loaded);
        Assert.Equal("Renamed", new ReportLayoutStore(_paths, _log).Find(saved.Id)?.Name);

        Assert.True(reopened.Delete(saved.Id));
        Assert.Null(reopened.FindDefault());
        Assert.Empty(new ReportLayoutStore(_paths, _log).Layouts);
    }

    [Fact]
    public async Task SvfRendererWritesHtmlForTheChosenLayout()
    {
        var renderer = new SvfReportRenderer();
        var layout = ReportLayoutStore.CreateDefault("Standard invoice");
        var directory = Path.Combine(_sandbox, "exports");

        var result = await renderer.RenderAsync(
            Document(3),
            new ReportExportOptions
            {
                Formats = ReportFormats.Html,
                OutputDirectory = directory,
                BaseName = "invoice",
                Layout = layout,
            });

        Assert.Equal("svf", renderer.Id);
        var file = Assert.Single(result.Files);
        Assert.Equal(Path.Combine(directory, "invoice.html"), file);
        Assert.Contains("Accounts form", await File.ReadAllTextAsync(file), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SvfRendererRequiresALayout()
    {
        var renderer = new SvfReportRenderer();

        await Assert.ThrowsAsync<ReportExportException>(() => renderer.RenderAsync(
            Document(1),
            new ReportExportOptions
            {
                Formats = ReportFormats.Html,
                OutputDirectory = Path.Combine(_sandbox, "exports"),
                BaseName = "invoice",
            }));
    }

    private static ReportDocument Document(int rows)
    {
        var document = new ReportDocument
        {
            Title = "Accounts form",
            Org = "e2e@example.com",
            Source = "SfUi.Tests",
        };

        var data = Enumerable.Range(0, rows)
            .Select(i => (IReadOnlyList<string?>)new[] { "row-" + i })
            .ToList();

        document.Sections.Add(ReportSection.FromTable(new ReportTable
        {
            Name = "Accounts",
            Columns = new[] { "Name" },
            Rows = data,
        }));

        return document;
    }

    private static int CountOccurrences(string text, string value)
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
