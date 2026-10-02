using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class CommandLineParserTests
{
    [Fact]
    public void Tokenize_SplitsOnWhitespace()
    {
        Assert.Equal(new[] { "org", "list" }, CommandLineParser.Tokenize("org list"));
        Assert.Equal(new[] { "org", "list" }, CommandLineParser.Tokenize("  org   list  "));
    }

    [Fact]
    public void Tokenize_KeepsQuotedStringsTogether()
    {
        Assert.Equal(
            new[] { "data", "query", "--query", "SELECT Id FROM Account" },
            CommandLineParser.Tokenize("data query --query \"SELECT Id FROM Account\""));

        Assert.Equal(
            new[] { "project", "deploy", "start", "-d", "C:\\my folder" },
            CommandLineParser.Tokenize("project deploy start -d 'C:\\my folder'"));
    }

    [Fact]
    public void Tokenize_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(CommandLineParser.Tokenize(""));
        Assert.Empty(CommandLineParser.Tokenize("   "));
    }

    [Theory]
    [InlineData("sf org list", new[] { "org", "list" })]
    [InlineData("sf.cmd org list", new[] { "org", "list" })]
    [InlineData("sf.exe org list", new[] { "org", "list" })]
    [InlineData("org list", new[] { "org", "list" })]
    [InlineData("sf", new string[0])]
    public void SplitSfArguments_RemovesLeadingSf(string input, string[] expected)
    {
        Assert.Equal(expected, CommandLineParser.SplitSfArguments(input));
    }
}
