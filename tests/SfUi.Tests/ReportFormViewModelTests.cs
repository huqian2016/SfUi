using SfUi.App.ViewModels;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>帳票出力ダイアログ（ReportFormViewModel）のレイアウト編集・保存・出力のテスト。</summary>
[Collection("Localization")]
public class ReportFormViewModelTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly ReportLayoutStore _layouts;

    public ReportFormViewModelTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
        _layouts = new ReportLayoutStore(_paths, _log);
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
    public void LoadSeedsADefaultLayoutAndFillsTheFields()
    {
        var vm = new ReportFormViewModel(_layouts, _paths);

        vm.Load(Document(2), "form-1");

        Assert.Single(vm.LayoutOptions);
        Assert.Equal(vm.LayoutOptions[0].Id, vm.SelectedLayoutId);
        Assert.False(string.IsNullOrWhiteSpace(vm.LayoutName));
        Assert.Contains("{Title}", vm.HeaderText, StringComparison.Ordinal);
        Assert.Equal("A4", vm.PaperSize);
        Assert.Equal("portrait", vm.Orientation);
        Assert.Equal("form-1", vm.OutputName);
        Assert.Contains("form-1", vm.OutputHint, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingKeepsTheEditsAndRenamesTheOption()
    {
        var vm = new ReportFormViewModel(_layouts, _paths);
        vm.Load(Document(1), "form-1");

        vm.LayoutName = "Invoice";
        vm.HeaderText = "{Title} / {Org} / {Date}";
        vm.SummaryText = "Rows: {Rows}";
        vm.FooterText = "{Page} / {Pages}";
        vm.PaperSize = ReportPapers.A3;
        vm.Orientation = "landscape";
        vm.MarginMm = 20;
        var saved = vm.SaveCurrentLayout();

        Assert.Equal("Invoice", saved.Name);
        Assert.Equal(ReportPapers.A3, saved.PaperSize);
        Assert.Equal("landscape", saved.Orientation);
        Assert.Equal(20, saved.MarginMm);
        Assert.Equal("Invoice", new ReportLayoutStore(_paths, _log).Find(saved.Id)?.Name);
        Assert.Equal("Invoice", vm.LayoutOptions[0].Label);
    }

    [Fact]
    public void NewLayoutAddsAnOptionAndDeleteRemovesIt()
    {
        var vm = new ReportFormViewModel(_layouts, _paths);
        vm.Load(Document(1), "form-1");

        vm.NewLayoutCommand.Execute(null);
        Assert.Equal(2, vm.LayoutOptions.Count);

        vm.DeleteLayoutCommand.Execute(null);
        Assert.Single(vm.LayoutOptions);
    }

    [Fact]
    public async Task RenderWritesHtmlToExports()
    {
        var vm = new ReportFormViewModel(_layouts, _paths);
        vm.Load(Document(3), "form-1");
        vm.WriteHtml = true;
        vm.WritePdf = false;

        var result = await vm.RenderInternalAsync();

        Assert.NotNull(result);
        var file = Assert.Single(result!.Files);
        Assert.Equal(Path.Combine(_paths.ExportsDirectory, "form-1.html"), file);
        Assert.True(File.Exists(file));
        var html = await File.ReadAllTextAsync(file);
        Assert.Contains("<table class=\"detail\">", html, StringComparison.Ordinal);
        Assert.Contains("row-2", html, StringComparison.Ordinal);
        Assert.Contains(UiText.T("Report_DoneFmt", 1, _paths.ExportsDirectory), vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderRequiresAtLeastOneFormat()
    {
        var vm = new ReportFormViewModel(_layouts, _paths);
        vm.Load(Document(1), "form-1");
        vm.WriteHtml = false;
        vm.WritePdf = false;

        var result = await vm.RenderInternalAsync();

        Assert.Null(result);
        Assert.Equal(UiText.T("Report_NoFormats"), vm.StatusText);
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
            Columns = new[] { "Name" },
            Rows = Enumerable.Range(0, rows).Select(i => (IReadOnlyList<string?>)new[] { "row-" + i }).ToList(),
        }));

        return document;
    }
}
