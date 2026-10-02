using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class SfCliRunnerTests
{
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("--json", "--json")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    public void QuoteArgument_SimpleCases(string input, string expected)
    {
        Assert.Equal(expected, SfCliRunner.QuoteArgument(input));
    }

    [Fact]
    public void QuoteArgument_EscapesInnerQuotes()
    {
        // 入力: a"b → 出力: "a\"b"
        var expected = "\"" + "a" + "\\" + "\"" + "b" + "\"";
        Assert.Equal(expected, SfCliRunner.QuoteArgument("a\"b"));
    }

    [Fact]
    public void QuoteArgument_DoublesTrailingBackslashesInsideQuotes()
    {
        // 入力: C:\path with space\ → 出力: "C:\path with space\\"
        var expected = "\"" + "C:\\path with space" + "\\\\" + "\"";
        Assert.Equal(expected, SfCliRunner.QuoteArgument("C:\\path with space\\"));
    }

    [Fact]
    public void QuoteArgument_HandlesBackslashBeforeInnerQuote()
    {
        // 入力: a\"b → 出力: "a\\\"b"
        var expected = "\"" + "a" + "\\\\\\" + "\"" + "b" + "\"";
        Assert.Equal(expected, SfCliRunner.QuoteArgument("a\\\"b"));
    }

    [Fact]
    public void BuildCommandLine_QuotesPathWithSpaces()
    {
        var commandLine = SfCliRunner.BuildCommandLine(
            "C:\\Program Files\\sf\\bin\\sf.cmd",
            new[] { "org", "list", "--json" });

        var expected = "\"" + "C:\\Program Files\\sf\\bin\\sf.cmd" + "\" org list --json";
        Assert.Equal(expected, commandLine);
    }

    [Fact]
    public void BuildCommandLine_QuotesQueryWithSpaces()
    {
        var commandLine = SfCliRunner.BuildCommandLine(
            "C:\\sf\\sf.cmd",
            new[] { "data", "query", "--query", "SELECT Id FROM Account" });

        Assert.Equal("C:\\sf\\sf.cmd data query --query \"SELECT Id FROM Account\"", commandLine);
    }

    [Fact]
    public void ToCmdArguments_WrapsCommandLine()
    {
        var commandLine = "\"" + "C:\\Program Files\\sf\\bin\\sf.cmd" + "\" org list --json";
        var expected = "/d /s /c \"" + commandLine + "\"";
        Assert.Equal(expected, SfCliRunner.ToCmdArguments(commandLine));
    }
}
