using SfUi.App.ViewModels;
using SfUi.Core;
using SfUi.Presentation;
using Xunit;

namespace SfUi.Tests;

/// <summary>レポート出力ダイアログの設定（前回値の復元・保存）を検証する。</summary>
[Collection("Localization")]
public sealed class ReportExportDialogViewModelTests
{
    private static ReportDocument CreateDocument()
    {
        var document = new ReportDocument { Title = "Sample report", Org = "demo@example.com" };
        document.Sections.Add(new ReportSection
        {
            Heading = "Account",
            Table = new ReportTable
            {
                Name = "Account",
                Columns = new List<string> { "Name" },
                Rows = new List<IReadOnlyList<string?>> { new[] { "Acme" } },
            },
        });
        return document;
    }

    [Fact]
    public void Constructor_RestoresSavedDefaults()
    {
        var store = CreateStore(out var sandbox);
        try
        {
            store.SaveReport(new ReportExportDefaults
            {
                Csv = false,
                Markdown = true,
                Html = true,
                IncludeAnalysis = false,
                MaxRows = 1200,
                OpenAfterSave = false,
                OutputDirectory = @"C:\saved\report",
            });

            var vm = new ReportExportDialogViewModel(CreateDocument(), "report", null, new FakeFilePicker(), store);

            Assert.False(vm.Csv);
            Assert.True(vm.Markdown);
            Assert.True(vm.Html);
            Assert.False(vm.IncludeAnalysis);
            Assert.Equal("1,200", vm.MaxRowsText);
            Assert.False(vm.OpenAfterSave);
            Assert.Equal(@"C:\saved\report", vm.OutputDirectory);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void BuildOptions_SavesDefaults_ForTheNextRun()
    {
        var store = CreateStore(out var sandbox);
        try
        {
            var vm = new ReportExportDialogViewModel(CreateDocument(), "report", null, new FakeFilePicker(), store);
            vm.Csv = false;
            vm.Pdf = true;
            vm.IncludeAnalysis = false;
            vm.MaxRowsText = "250";
            vm.OutputDirectory = @"C:\fresh\report";

            var options = vm.BuildOptions();

            Assert.NotNull(options);
            Assert.Equal(ReportFormats.Pdf, options!.Formats);

            var saved = store.LoadReport();
            Assert.False(saved.Csv);
            Assert.True(saved.Pdf);
            Assert.False(saved.IncludeAnalysis);
            Assert.Equal(250, saved.MaxRows);
            Assert.Equal(@"C:\fresh\report", saved.OutputDirectory);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void InitialDirectory_TakesPrecedenceOverSavedFolder()
    {
        var store = CreateStore(out var sandbox);
        try
        {
            store.SaveReport(new ReportExportDefaults { OutputDirectory = @"C:\saved\report" });

            var vm = new ReportExportDialogViewModel(CreateDocument(), "report", @"C:\explicit", new FakeFilePicker(), store);

            Assert.Equal(@"C:\explicit", vm.OutputDirectory);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void BuildOptions_AcceptsGroupedMaxRowsRestoredFromSettings()
    {
        var store = CreateStore(out var sandbox);
        try
        {
            store.SaveReport(new ReportExportDefaults { MaxRows = 1200, OutputDirectory = @"C:\saved\report" });

            var vm = new ReportExportDialogViewModel(CreateDocument(), "report", null, new FakeFilePicker(), store);
            Assert.Equal("1,200", vm.MaxRowsText);

            var options = vm.BuildOptions();

            Assert.NotNull(options);
            Assert.Equal(1200, options!.MaxRows);
            Assert.Null(vm.ErrorText);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    private static ExportSettingsStore CreateStore(out string sandbox)
    {
        sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var paths = AppPaths.Resolve(dataRootOverride: sandbox, baseDirectory: sandbox, appDataDirectory: sandbox);
        return new ExportSettingsStore(paths, new AppLog(paths));
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, string filter, string? initialDirectory = null)
            => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedFileName, string filter, string? initialDirectory = null)
            => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title, string? initialDirectory = null)
            => Task.FromResult<string?>(null);
    }
}
