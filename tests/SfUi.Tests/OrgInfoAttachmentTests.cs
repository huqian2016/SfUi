using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>UI 文字列（切り詰めメッセージ）を検証するため、ローカライズ系テストと直列実行する。</summary>
[Collection("Localization")]
public class OrgInfoAttachmentTests
{
    [Fact]
    public void Build_FormatsTitleHeaderAndRows()
    {
        var text = OrgInfoAttachment.Build(
            "Users / Fetched: 2026-10-03",
            new[] { "Name", "Email" },
            new[] { new[] { "Taro", "taro@example.com" }, new[] { "Hanako", "hanako@example.com" } });

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal(4, lines.Length);
        Assert.Equal("Users / Fetched: 2026-10-03", lines[0]);
        Assert.Equal("Name\tEmail", lines[1]);
        Assert.Equal("Taro\ttaro@example.com", lines[2]);
        Assert.Equal("Hanako\thanako@example.com", lines[3]);
    }

    [Fact]
    public void Build_TruncatesRowsAtMaxChars()
    {
        var rows = Enumerable.Range(1, 100).Select(i => new[] { $"row{i}", new string('x', 50) }).ToList();

        var text = OrgInfoAttachment.Build("Big", new[] { "A", "B" }, rows, maxChars: 500);

        Assert.True(text.Length < 700, $"length={text.Length}");
        Assert.Contains("…", text);
        Assert.DoesNotContain("row100", text);
    }

    [Fact]
    public void Build_SanitizesTabsAndNewlines()
    {
        var text = OrgInfoAttachment.Build(
            "Tabs\tin title",
            new[] { "A\nB" },
            new[] { new[] { "line1\r\nline2" } });

        var lines = text.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Contains("Tabs in title", lines[0]);
        Assert.Contains("A B", lines[1]);
        Assert.Contains("line1  line2", lines[2]);
    }
}
