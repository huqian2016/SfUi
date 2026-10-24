using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>ER 図の SVG / PNG / JPEG / WebP レンダラー。</summary>
public class ErdImageRendererTests
{
    private static ErdDocument CreateDocument()
    {
        var account = new ErdEntity
        {
            ApiName = "Account",
            Label = "取引先",
            Fields = new List<ErdField>
            {
                new() { ApiName = "Id", DataType = "Id", IsPrimaryKey = true },
                new() { ApiName = "Name", Label = "取引先名", DataType = "string" },
                new() { ApiName = "OwnerId", DataType = "reference", ReferenceTo = new[] { "User" } },
            },
        };
        var contact = new ErdEntity
        {
            ApiName = "Contact",
            Label = "担当者",
            Fields = new List<ErdField>
            {
                new() { ApiName = "Id", DataType = "Id", IsPrimaryKey = true },
                new() { ApiName = "AccountId", DataType = "reference", ReferenceTo = new[] { "Account" }, IsRequired = true },
            },
        };

        return new ErdDocument
        {
            Title = "Sample ERD",
            Entities = new[] { account, contact },
            Relationships = new[]
            {
                new ErdRelationship { ChildApiName = "Contact", ParentApiName = "Account", FieldApiName = "AccountId", IsRequired = true },
                new ErdRelationship { ChildApiName = "Contact", ParentApiName = "Missing__c", FieldApiName = "Missing" },
            },
        };
    }

    [Fact]
    public void Svg_ContainsNodesEdgesAndJapaneseLabels()
    {
        var svg = ErdImageRenderer.WriteSvg(CreateDocument());

        Assert.StartsWith("<?xml", svg);
        Assert.Contains("<svg", svg);
        Assert.Contains("取引先 (Account)", svg);
        Assert.Contains("id=\"Account\"", svg);
        Assert.Contains("id=\"edges\"", svg);
        Assert.Contains("AccountId", svg);
        Assert.DoesNotContain("Missing__c", svg);
    }

    [Fact]
    public void Layout_PlacesNodesInGrid_AndSkipsUnknownTargets()
    {
        var layout = ErdImageRenderer.BuildLayout(CreateDocument());

        Assert.Equal(2, layout.Nodes.Count);
        Assert.Single(layout.Edges);                       // Missing__c is skipped
        Assert.True(layout.Width > 300);
        Assert.True(layout.Height > 100);
        Assert.All(layout.Nodes, node => Assert.True(node.X >= 40 && node.Y >= 40));
    }

    [Fact]
    public void Png_HasValidSignature()
    {
        var bytes = ErdImageRenderer.Render(CreateDocument(), ErdImageFormat.Png);

        Assert.True(bytes.Length > 1000);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4).ToArray());

        WriteSample(bytes, "png");
    }

    [Fact]
    public void Jpeg_HasValidSignature()
    {
        var bytes = ErdImageRenderer.Render(CreateDocument(), ErdImageFormat.Jpeg);

        Assert.True(bytes.Length > 1000);
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, bytes.Take(2).ToArray());
    }

    [Fact]
    public void Svg_Render_ReturnsUtf8Text()
    {
        var bytes = ErdImageRenderer.Render(CreateDocument(), ErdImageFormat.Svg);
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("<svg", text);
        Assert.Equal("svg", ErdImageRenderer.FileExtension(ErdImageFormat.Svg));
    }

    /// <summary>手動確認用: 環境変数 SFUI_ERD_SAMPLE_DIR があれば PNG を書き出す。</summary>
    private static void WriteSample(byte[] bytes, string extension)
    {
        var dir = Environment.GetEnvironmentVariable("SFUI_ERD_SAMPLE_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "erd-sample." + extension), bytes);
    }
}
