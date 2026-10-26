using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>出力ダイアログの設定（data/export-settings.json）の保存・復元。</summary>
public class ExportSettingsStoreTests : IDisposable
{
    private readonly string _sandbox;

    public ExportSettingsStoreTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
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

    private AppPaths Paths => AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);

    private ExportSettingsStore CreateStore() => new(Paths, new AppLog(Paths));

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = CreateStore().Load();

        Assert.Equal(ExportSettingsStore.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.True(settings.Erd.Mermaid);
        Assert.True(settings.Erd.Svg);
        Assert.False(settings.Erd.Html);
        Assert.Equal(ErdNameStyle.LabelAndApiName, settings.Erd.NameStyle);
        Assert.Null(settings.Erd.OutputDirectory);
        Assert.True(settings.Report.Csv);
        Assert.Equal(5000, settings.Report.MaxRows);
        Assert.Null(settings.Report.OutputDirectory);
    }

    [Fact]
    public void SaveErd_RoundTrips()
    {
        var store = CreateStore();
        store.SaveErd(new ErdExportDefaults
        {
            NameStyle = ErdNameStyle.ApiName,
            IncludeStandardObjects = false,
            IncludeReferencedParents = false,
            Mermaid = false,
            Dbml = true,
            Html = true,
            Svg = false,
            Png = true,
            OpenAfterSave = true,
            OutputDirectory = @"C:\out\erd",
        });

        var restored = CreateStore().LoadErd();

        Assert.Equal(ErdNameStyle.ApiName, restored.NameStyle);
        Assert.False(restored.IncludeStandardObjects);
        Assert.False(restored.IncludeReferencedParents);
        Assert.False(restored.Mermaid);
        Assert.True(restored.Dbml);
        Assert.True(restored.Html);
        Assert.False(restored.Svg);
        Assert.True(restored.Png);
        Assert.True(restored.OpenAfterSave);
        Assert.Equal(@"C:\out\erd", restored.OutputDirectory);
    }

    [Fact]
    public void SaveReport_RoundTrips_AndKeepsErdSection()
    {
        var store = CreateStore();
        store.SaveErd(new ErdExportDefaults { Html = true, OutputDirectory = @"C:\out\erd" });

        store.SaveReport(new ReportExportDefaults
        {
            Csv = false,
            Markdown = true,
            Html = true,
            Excel = true,
            Pdf = true,
            IncludeAnalysis = false,
            MaxRows = 1200,
            OpenAfterSave = false,
            OutputDirectory = @"C:\out\report",
        });

        var restored = CreateStore();
        var report = restored.LoadReport();
        Assert.False(report.Csv);
        Assert.True(report.Markdown);
        Assert.True(report.Html);
        Assert.True(report.Excel);
        Assert.True(report.Pdf);
        Assert.False(report.IncludeAnalysis);
        Assert.Equal(1200, report.MaxRows);
        Assert.False(report.OpenAfterSave);
        Assert.Equal(@"C:\out\report", report.OutputDirectory);

        // ER 図側の設定は消えない
        var erd = restored.LoadErd();
        Assert.True(erd.Html);
        Assert.Equal(@"C:\out\erd", erd.OutputDirectory);
    }

    [Fact]
    public void Save_SanitizesInvalidValues()
    {
        var store = CreateStore();
        var settings = new ExportSettings
        {
            Erd = new ErdExportDefaults { NameStyle = (ErdNameStyle)99, OutputDirectory = "   " },
            Report = new ReportExportDefaults { MaxRows = -10, OutputDirectory = "  C:\\out  " },
        };

        store.Save(settings);
        var restored = CreateStore().Load();

        Assert.Equal(ErdNameStyle.LabelAndApiName, restored.Erd.NameStyle);
        Assert.Null(restored.Erd.OutputDirectory);
        Assert.Equal(0, restored.Report.MaxRows);
        Assert.Equal(@"C:\out", restored.Report.OutputDirectory);
    }

    [Fact]
    public void Load_OldSchema_ResetsToDefaults()
    {
        var store = CreateStore();
        store.Save(new ExportSettings { Erd = new ErdExportDefaults { Html = true } });

        var json = File.ReadAllText(store.FilePath);
        File.WriteAllText(store.FilePath, json.Replace(
            $"\"schemaVersion\": {ExportSettingsStore.CurrentSchemaVersion}",
            "\"schemaVersion\": 0"));

        var restored = CreateStore().Load();

        Assert.Equal(ExportSettingsStore.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.False(restored.Erd.Html);
    }

    [Fact]
    public void Load_BrokenFile_ReturnsDefaults()
    {
        var store = CreateStore();
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "{ this is not json");

        var restored = CreateStore().Load();

        Assert.True(restored.Erd.Mermaid);
        Assert.Equal(5000, restored.Report.MaxRows);
    }
}
