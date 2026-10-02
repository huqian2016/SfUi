using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class SfCommandSafetyTests
{
    [Theory]
    [InlineData(new[] { "project", "deploy", "start" }, true, "deploy")]
    [InlineData(new[] { "data", "delete", "record" }, true, "delete")]
    [InlineData(new[] { "org", "logout" }, true, "logout")]
    [InlineData(new[] { "org", "refresh", "sandbox" }, true, "refresh")]
    [InlineData(new[] { "package", "uninstall" }, true, "uninstall")]
    [InlineData(new[] { "org", "list" }, false, null)]
    [InlineData(new[] { "data", "query", "--query", "SELECT Id FROM Account" }, false, null)]
    public void IsDangerous_DetectsDangerousTokens(string[] arguments, bool expected, string? expectedToken)
    {
        var actual = SfCommandSafety.IsDangerous(arguments, out var matched);

        Assert.Equal(expected, actual);
        Assert.Equal(expectedToken, matched);
    }

    [Fact]
    public void IsDangerous_IsCaseInsensitive_AndIgnoresFlags()
    {
        Assert.True(SfCommandSafety.IsDangerous(new[] { "PROJECT", "Deploy", "start" }, out _));
        Assert.False(SfCommandSafety.IsDangerous(new[] { "--delete" }, out _));
    }
}
