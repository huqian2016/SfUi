using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>ANSI エスケープ シーケンス除去（Command タブの org list 表示崩れ修正）のテスト。</summary>
public class AnsiTextTests
{
    [Fact]
    public void Strip_RemovesSgrColorCodes()
    {
        var input = "\u001B[96mDevHub\u001B[39m \u001B[32mConnected\u001B[39m";
        Assert.Equal("DevHub Connected", AnsiText.Strip(input));
    }

    [Fact]
    public void Strip_RemovesOscTitleSequence()
    {
        var input = "\u001B]0;sf org list\u0007row1";
        Assert.Equal("row1", AnsiText.Strip(input));
    }

    [Fact]
    public void Strip_RemovesTwoCharEscapes()
    {
        var input = "\u001B(Bplain";
        Assert.Equal("plain", AnsiText.Strip(input));
    }

    [Fact]
    public void Strip_RemovesAllEscapesFromOrgListSample()
    {
        // sf org list を SfUi の Command タブで実行したときの実測サンプル
        var sample = "\u001B[96mDevHub\u001B[39m  \u001B[95macc\u001B[39m \u001B[93mSandbox\u001B[39m \u001B[31mDomainNotFoundError\u001B[39m";
        var stripped = AnsiText.Strip(sample) ?? string.Empty;
        Assert.DoesNotContain('\u001B', stripped);
        Assert.DoesNotContain("[96m", stripped);
        Assert.Contains("DomainNotFoundError", stripped);
    }

    [Fact]
    public void Strip_PassesThroughPlainText()
    {
        Assert.Equal("no codes here", AnsiText.Strip("no codes here"));
        Assert.Null(AnsiText.Strip(null));
        Assert.Equal(string.Empty, AnsiText.Strip(string.Empty));
    }
}
