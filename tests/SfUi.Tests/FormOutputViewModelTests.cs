using SfUi.App.ViewModels;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>帳票テンプレート出力ダイアログ（FormOutputViewModel）のテスト。</summary>
[Collection("Localization")]
public class FormOutputViewModelTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly FormStore _store;

    public FormOutputViewModelTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
        _store = new FormStore(_paths, _log);
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
    public void LoadListsEveryTemplateAndPicksTheGeneralOne()
    {
        var vm = new FormOutputViewModel(_store, _paths, _log);

        vm.Load(Document(3), "form-1");

        Assert.Equal(FormTemplateLibrary.All.Count, vm.Forms.Count);
        Assert.Equal(FormTemplateLibrary.All[0].Id, vm.SelectedForm?.Id);
        Assert.Equal("form-1", vm.OutputName);
        Assert.True(vm.HasPreview);
        Assert.Contains("form-1", vm.OutputHint, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectingAnEstimateTemplateCreatesMergeFields()
    {
        var vm = new FormOutputViewModel(_store, _paths, _log);
        vm.Load(Document(3), "form-1");

        vm.SelectedForm = FormTemplateLibrary.Find("t_estimate_manufacturing");

        var keys = vm.Values.Select(v => v.Key).ToList();
        Assert.Contains("CustomerName", keys);
        Assert.Contains("QuoteNo", keys);
        Assert.Contains("TotalAmount", keys);
        Assert.DoesNotContain("Title", keys);
        Assert.DoesNotContain("Page", keys);
        Assert.Contains(vm.Values, v => v.Label == "Customer Name");
        Assert.True(vm.HasPreview);
    }

    [Fact]
    public void MergeValuesReachTheRenderedForm()
    {
        var vm = new FormOutputViewModel(_store, _paths, _log);
        vm.Load(Document(3), "form-1");
        vm.SelectedForm = FormTemplateLibrary.Find("t_estimate_manufacturing");
        vm.Values.First(v => v.Key == "CustomerName").Value = "株式会社テスト";

        var data = vm.BuildFormData();
        var html = FormHtmlWriter.Write(vm.SelectedForm!, data);

        Assert.True(data.Values.ContainsKey("CustomerName"));
        Assert.Contains("株式会社テスト", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, false, false, false, ReportFormats.Html)]
    [InlineData(false, true, false, false, false, ReportFormats.Pdf)]
    [InlineData(false, false, true, false, false, ReportFormats.Svg)]
    [InlineData(false, false, false, true, true, ReportFormats.Png | ReportFormats.Jpeg)]
    [InlineData(false, false, false, false, false, ReportFormats.None)]
    public void FormatFlagsFollowTheCheckboxes(bool html, bool pdf, bool svg, bool png, bool jpeg, ReportFormats expected)
    {
        var vm = new FormOutputViewModel(_store, _paths, _log)
        {
            WriteHtml = html,
            WritePdf = pdf,
            WriteSvg = svg,
            WritePng = png,
            WriteJpeg = jpeg,
        };

        Assert.Equal(expected, vm.SelectedFormatFlags());
    }

    [Fact]
    public async Task RenderWritesTheSelectedFormats()
    {
        var vm = new FormOutputViewModel(_store, _paths, _log);
        vm.Load(Document(3), "form-1");
        vm.WriteHtml = true;
        vm.WritePdf = false;
        vm.WriteSvg = true;
        vm.WritePng = false;

        var result = await vm.RenderInternalAsync();

        Assert.NotNull(result);
        Assert.Equal(2, result!.Files.Count);
        Assert.Contains(result.Files, f => f.EndsWith("form-1.html", StringComparison.Ordinal));
        Assert.Contains(result.Files, f => f.EndsWith("form-1.svg", StringComparison.Ordinal));
        Assert.Contains("Saved", vm.StatusText.Length > 0 ? "Saved" : vm.StatusText, StringComparison.Ordinal);
        Assert.Equal(2, vm.LastFiles.Count);
    }

    [Fact]
    public async Task RenderRequiresAFormat()
    {
        var vm = new FormOutputViewModel(_store, _paths, _log);
        vm.Load(Document(3), "form-1");
        vm.WriteHtml = false;
        vm.WritePdf = false;

        var result = await vm.RenderInternalAsync();

        Assert.Null(result);
        Assert.Equal(UiText.T("Report_NoFormats"), vm.StatusText);
    }

    [Fact]
    public void PrintPagesRenderOneImagePerPage()
    {
        var vm = new FormOutputViewModel(_store, _paths, _log);
        vm.Load(Document(120), "form-1");

        var pages = vm.RenderPrintPages();

        Assert.True(pages.Count >= 2, $"expected multiple pages, got {pages.Count}");
        Assert.All(pages, png => Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray()));
    }

    private static ReportDocument Document(int rows)
    {
        var document = new ReportDocument
        {
            Title = "Accounts",
            Org = "e2e@example.com",
            Source = "SfUi.Tests",
        };

        document.Sections.Add(ReportSection.FromTable(new ReportTable
        {
            Name = "Accounts",
            Columns = new[] { "品名", "数量", "金額" },
            Rows = Enumerable.Range(0, rows)
                .Select(i => (IReadOnlyList<string?>)new[] { "item-" + i, "1", "400" })
                .ToList(),
            NumericColumns = new HashSet<int> { 1, 2 },
        }));

        return document;
    }
}
