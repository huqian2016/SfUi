using SfUi.Core;
using SfUi.Presentation;
using Xunit;

namespace SfUi.Tests;

/// <summary>レポート出力（Markdown / HTML / Excel / CSV / 分析 / 行数制限）のテスト。</summary>
[Collection("Localization")]
public class ReportExportTests : IDisposable
{
    private readonly string _dir;

    public ReportExportTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sfui-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // 後始末の失敗は無視
        }
    }

    private static ReportDocument CreateDocument() =>
        ReportDocumentFactory.FromTable(
            "Accounts",
            "dev@example.com",
            "SELECT Id, Name FROM Account",
            new ReportTable
            {
                Name = "Account",
                Columns = new[] { "Id", "Name", "Amount" },
                NumericColumns = new HashSet<int> { 2 },
                Rows = new List<IReadOnlyList<string?>>
                {
                    new[] { "001A", "Acme|Corp", "100" },
                    new[] { "001B", "Beta", "250.5" },
                    new[] { "001B", "Beta", "250.5" },
                },
            },
            new ReportSection { Heading = "Query", Text = "SELECT Id, Name FROM Account" });

    [Fact]
    public void Markdown_HasFrontMatterAndTable()
    {
        var markdown = ReportMarkdownWriter.Write(CreateDocument());

        Assert.StartsWith("---\n", markdown, StringComparison.Ordinal);
        Assert.Contains("title: \"Accounts\"", markdown);
        Assert.Contains("org: \"dev@example.com\"", markdown);
        Assert.Contains("# Accounts", markdown);
        Assert.Contains("| Id | Name | Amount |", markdown);
        // パイプは表を壊さないようエスケープされる
        Assert.Contains("Acme\\|Corp", markdown);
        // 数値列は右寄せ指定
        Assert.Contains("---:", markdown);
    }

    [Fact]
    public void Html_IsSelfContainedWithFilterAndSort()
    {
        var html = ReportHtmlWriter.Write(CreateDocument());

        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.Contains("Filter rows", html);
        Assert.Contains("addEventListener('click'", html.Replace("\"", "'"));
        Assert.Contains("<table class=\"grid\"", html);
        Assert.Contains("001A", html);
    }

    [Fact]
    public void Html_EscapesMarkup()
    {
        var document = ReportDocumentFactory.FromTable(
            "<script>alert(1)</script>",
            null,
            null,
            new ReportTable
            {
                Name = "T",
                Columns = new[] { "C" },
                Rows = new List<IReadOnlyList<string?>> { new[] { "<b>x</b>" } },
            });

        var html = ReportHtmlWriter.Write(document);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html);
    }

    [Fact]
    public void Analysis_ComputesTotalsAndDuplicates()
    {
        var table = new ReportTable
        {
            Name = "Account",
            Columns = new[] { "Name", "Amount" },
            NumericColumns = new HashSet<int> { 1 },
            Rows = new List<IReadOnlyList<string?>>
            {
                new[] { "A", "100" },
                new[] { "B", "300" },
                new[] { "A", "100" },
                new[] { "C", null },
            },
        };

        var section = ReportAnalysis.Analyze(table);
        var text = string.Join('\n', section.Bullets!);

        Assert.Contains(UiText.T("Report_AnalysisRowsFmt", "4", "2"), text);
        Assert.Contains("500", text);
        Assert.Contains(UiText.T("Report_AnalysisDuplicatesFmt", "1"), text);
    }

    [Fact]
    public void RowLimit_TruncatesTables()
    {
        var limited = CreateDocument().WithRowLimit(1);

        Assert.Equal(1, limited.Tables.Single().RowCount);
    }

    [Fact]
    public async Task Export_WritesCsvMarkdownHtmlAndExcel()
    {
        var options = new ReportExportOptions
        {
            Formats = ReportFormats.Csv | ReportFormats.Markdown | ReportFormats.Html | ReportFormats.Excel,
            IncludeAnalysis = true,
            MaxRows = 0,
            OutputDirectory = _dir,
            BaseName = "report",
        };

        var result = await ReportExportService.ExportAsync(CreateDocument(), options);

        Assert.Equal(4, result.Files.Count);
        Assert.All(result.Files, file => Assert.True(File.Exists(file), file));
        Assert.Contains(result.Files, file => file.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Files, file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Files, file => file.EndsWith(".html", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Files, file => file.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));

        var markdown = await File.ReadAllTextAsync(result.Files.Single(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(UiText.T("Report_AnalysisHeadingFmt", "Account"), markdown);
        var csv = await File.ReadAllTextAsync(result.Files.Single(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("Acme|Corp", csv.Replace("\"", string.Empty));
    }

    [Fact]
    public async Task Export_Csv_UsesOneFilePerTable()
    {
        var document = CreateDocument();
        document.Sections.Add(ReportSection.FromTable(new ReportTable
        {
            Name = "Second",
            Columns = new[] { "X" },
            Rows = new List<IReadOnlyList<string?>> { new[] { "1" } },
        }));

        var result = await ReportExportService.ExportAsync(document, new ReportExportOptions
        {
            Formats = ReportFormats.Csv,
            IncludeAnalysis = false,
            OutputDirectory = _dir,
            BaseName = "multi",
        });

        Assert.Equal(2, result.Files.Count);
        Assert.Contains(result.Files, file => file.Contains("multi-Account", StringComparison.Ordinal));
        Assert.Contains(result.Files, file => file.Contains("multi-Second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Export_WithoutFormats_ReturnsEmpty()
    {
        var result = await ReportExportService.ExportAsync(CreateDocument(), new ReportExportOptions
        {
            Formats = ReportFormats.None,
            OutputDirectory = _dir,
            BaseName = "none",
        });

        Assert.Empty(result.Files);
    }
}
