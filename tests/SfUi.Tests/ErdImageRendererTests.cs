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
    public void Svg_DefinesCrowFootMarkersForEachEdgeStyle()
    {
        var svg = ErdImageRenderer.WriteSvg(CreateDocument());

        Assert.Contains("<marker id=\"crow-detail\"", svg);
        Assert.Contains("<marker id=\"crow-required\"", svg);
        Assert.Contains("<marker id=\"crow-optional\"", svg);
        Assert.Contains("url(#crow-", svg);
        Assert.DoesNotContain("url(#crow)", svg);
    }

    [Fact]
    public void Layout_SkipsUnknownTargets_AndKeepsNodesInsideTheCanvas()
    {
        var layout = ErdImageRenderer.BuildLayout(CreateDocument());

        Assert.Equal(2, layout.Nodes.Count);
        Assert.Single(layout.Edges);                       // Missing__c is skipped
        Assert.True(layout.Width > 300);
        Assert.True(layout.Height > 100);
        Assert.All(layout.Nodes, node => Assert.True(node.X >= 40 && node.Y >= 40));
    }

    [Fact]
    public void Layout_IsRadial_WithTheMostConnectedNodeInTheMiddle()
    {
        var document = CreateHubDocument(leafCount: 10);
        var layout = ErdImageRenderer.BuildLayout(document);

        Assert.Equal(11, layout.Nodes.Count);
        var hub = layout.Nodes.Single(node => node.Entity.ApiName == "Hub__c");
        var hubCenterX = hub.X + (hub.Width / 2);
        var hubCenterY = hub.Y + (hub.Height / 2);

        // 関連の最も多い Hub が図の中央付近に来る
        Assert.InRange(hubCenterX, layout.Width * 0.35f, layout.Width * 0.65f);
        Assert.InRange(hubCenterY, layout.Height * 0.35f, layout.Height * 0.65f);

        // ノード同士は重ならない
        for (var i = 0; i < layout.Nodes.Count; i++)
        {
            for (var j = i + 1; j < layout.Nodes.Count; j++)
            {
                var a = layout.Nodes[i];
                var b = layout.Nodes[j];
                var overlap = Math.Abs((a.X + (a.Width / 2)) - (b.X + (b.Width / 2))) < ((a.Width + b.Width) / 2) &&
                              Math.Abs((a.Y + (a.Height / 2)) - (b.Y + (b.Height / 2))) < ((a.Height + b.Height) / 2);
                Assert.False(overlap, $"{a.Entity.ApiName} overlaps {b.Entity.ApiName}");
            }
        }

        // 次数の少ないリーフは中心から離れている
        var leaves = layout.Nodes.Where(node => node.Entity.ApiName != "Hub__c").ToList();
        Assert.All(leaves, leaf =>
        {
            var distance = MathF.Sqrt(
                MathF.Pow((leaf.X + (leaf.Width / 2)) - hubCenterX, 2) +
                MathF.Pow((leaf.Y + (leaf.Height / 2)) - hubCenterY, 2));
            Assert.True(distance > hub.Width / 2, leaf.Entity.ApiName);
        });
    }

    /// <summary>Hub 1 件に Leaf が放射状にぶら下がる文書（放射状レイアウトの検証用）。</summary>
    private static ErdDocument CreateHubDocument(int leafCount)
    {
        var entities = new List<ErdEntity>
        {
            new()
            {
                ApiName = "Hub__c",
                Label = "Hub",
                Fields = new List<ErdField> { new() { ApiName = "Id", DataType = "Id", IsPrimaryKey = true } },
            },
        };
        var relationships = new List<ErdRelationship>();
        for (var i = 0; i < leafCount; i++)
        {
            var name = "Leaf" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "__c";
            entities.Add(new ErdEntity
            {
                ApiName = name,
                Label = name,
                Fields = new List<ErdField>
                {
                    new() { ApiName = "Id", DataType = "Id", IsPrimaryKey = true },
                    new() { ApiName = "Hub__c", DataType = "reference", ReferenceTo = new[] { "Hub__c" } },
                },
            });
            relationships.Add(new ErdRelationship
            {
                ChildApiName = name,
                ParentApiName = "Hub__c",
                FieldApiName = "Hub__c",
                IsMasterDetail = true,
                IsRequired = true,
            });
        }

        return new ErdDocument { Title = "Hub", Entities = entities, Relationships = relationships };
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
