using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>SOQL スクリプト（-- コメント / ; 区切りの複数文）の分割。</summary>
public class SoqlScriptTests
{
    [Fact]
    public void Split_ReturnsSingleStatement_WithoutTrailingSemicolon()
    {
        var statements = SoqlScript.Split("SELECT Id FROM Account");

        var statement = Assert.Single(statements);
        Assert.Equal(0, statement.Index);
        Assert.Equal("SELECT Id FROM Account", statement.Text);
    }

    [Fact]
    public void Split_SplitsOnSemicolon_AndSkipsEmpty()
    {
        var statements = SoqlScript.Split("SELECT Id FROM Account;\n\nSELECT Id FROM Contact;\n");

        Assert.Equal(2, statements.Count);
        Assert.Equal("SELECT Id FROM Account", statements[0].Text);
        Assert.Equal("SELECT Id FROM Contact", statements[1].Text);
        Assert.Equal(1, statements[1].Index);
    }

    [Fact]
    public void Split_IgnoresSemicolonInsideStringLiteral()
    {
        var statements = SoqlScript.Split("SELECT Id FROM Account WHERE Name = 'a;b' ; SELECT Id FROM Contact");

        Assert.Equal(2, statements.Count);
        Assert.Contains("'a;b'", statements[0].Text);
    }

    [Fact]
    public void Split_StripsLineComments()
    {
        var text = "-- 先頭のコメント\nSELECT Id -- 行末\nFROM Account";

        var statement = Assert.Single(SoqlScript.Split(text));
        Assert.DoesNotContain("--", statement.Text);
        Assert.DoesNotContain("先頭", statement.Text);
        Assert.Contains("SELECT Id", statement.Text);
        Assert.Contains("FROM Account", statement.Text);
    }

    [Fact]
    public void Split_KeepsDoubleDashInsideStringLiteral()
    {
        var statement = Assert.Single(SoqlScript.Split("SELECT Id FROM Account WHERE Name = 'a--b'"));
        Assert.Contains("'a--b'", statement.Text);
    }

    [Fact]
    public void Split_StripsBlockComments()
    {
        var text = "SELECT Id /* ここは無視\nされる */ FROM Account";

        var statement = Assert.Single(SoqlScript.Split(text));
        Assert.DoesNotContain("ここは無視", statement.Text);
        Assert.Contains("FROM Account", statement.Text);
    }

    [Fact]
    public void Split_RespectsMaxStatements()
    {
        var text = string.Join(";", Enumerable.Range(0, 30).Select(i => $"SELECT Id FROM Obj{i}"));

        var statements = SoqlScript.Split(text);

        Assert.Equal(SoqlScript.MaxStatements, statements.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- only a comment")]
    [InlineData(";;;")]
    public void Split_ReturnsEmpty_ForBlankInput(string text)
    {
        Assert.Empty(SoqlScript.Split(text));
    }

    [Fact]
    public void StripComments_KeepsStringContentAndLineBreaks()
    {
        var stripped = SoqlScript.StripComments("SELECT Id -- x\r\nFROM Account");

        Assert.DoesNotContain("-- x", stripped);
        Assert.Contains("FROM Account", stripped);
        Assert.Contains("\n", stripped);
    }
}
