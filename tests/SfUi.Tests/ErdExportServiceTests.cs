using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>ER 図のファイル出力（テキスト / 画像）と検証。</summary>
public class ErdExportServiceTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "sfui-erd", Guid.NewGuid().ToString("N"));

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

    private static ErdDocument CreateDocument() => new()
    {
        Title = "Sample",
        Entities = new[]
        {
            new ErdEntity
            {
                ApiName = "Account",
                Label = "取引先",
                Fields = new List<ErdField> { new() { ApiName = "Id", DataType = "Id", IsPrimaryKey = true } },
            },
        },
    };

    [Fact]
    public void Export_WritesSelectedTextAndImageFormats()
    {
        var result = ErdExportService.Export(new ErdExportRequest
        {
            Document = CreateDocument(),
            OutputDirectory = _sandbox,
            BaseName = "erd-test",
            Mermaid = true,
            Dbml = true,
            Drawio = true,
            PlantUml = true,
            Dot = true,
            Svg = true,
            Png = true,
        });

        Assert.Equal(8, result.Files.Count);   // mmd + md + dbml + drawio + puml + dot + svg + png
        Assert.All(result.Files, file => Assert.True(File.Exists(file), file));
        Assert.Contains(result.Files, file => file.EndsWith("erd-test.md", StringComparison.Ordinal));
        Assert.Contains(result.Files, file => file.EndsWith("erd-test.drawio", StringComparison.Ordinal));

        var png = File.ReadAllBytes(result.Files.Single(file => file.EndsWith(".png", StringComparison.Ordinal)));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());

        var markdown = File.ReadAllText(result.Files.Single(file => file.EndsWith(".md", StringComparison.Ordinal)));
        Assert.Contains("```mermaid", markdown);
    }

    [Fact]
    public void Export_WritesHtml()
    {
        var result = ErdExportService.Export(new ErdExportRequest
        {
            Document = CreateDocument(),
            OutputDirectory = _sandbox,
            BaseName = "erd-html",
            Mermaid = false,
            Html = true,
        });

        var file = Assert.Single(result.Files);
        Assert.EndsWith("erd-html.html", file, StringComparison.Ordinal);
        var html = File.ReadAllText(file);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<svg", html);
    }

    [Fact]
    public void Export_Throws_WhenDocumentIsEmpty()
    {
        var error = Assert.Throws<ReportExportException>(() => ErdExportService.Export(new ErdExportRequest
        {
            Document = new ErdDocument { Title = "Empty" },
            OutputDirectory = _sandbox,
            BaseName = "empty",
        }));

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void Export_Throws_WhenNoFormatSelected()
    {
        var error = Assert.Throws<ReportExportException>(() => ErdExportService.Export(new ErdExportRequest
        {
            Document = CreateDocument(),
            OutputDirectory = _sandbox,
            BaseName = "none",
            Mermaid = false,
        }));

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void DescribeResult_MentionsCountAndFolder()
    {
        var result = ErdExportService.Export(new ErdExportRequest
        {
            Document = CreateDocument(),
            OutputDirectory = _sandbox,
            BaseName = "erd-describe",
            Mermaid = true,
        });

        var text = ErdExportService.DescribeResult(result);
        Assert.Contains("2", text);   // .mmd + .md
    }
}
