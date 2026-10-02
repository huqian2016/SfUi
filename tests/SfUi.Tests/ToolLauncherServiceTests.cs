using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class ToolLauncherServiceTests
{
    [Theory]
    [InlineData(@"C:\foo\bar", "/mnt/c/foo/bar")]
    [InlineData(@"C:\foo", "/mnt/c/foo")]
    [InlineData(@"C:\", "/mnt/c/")]
    [InlineData(@"d:\work space\proj", "/mnt/d/work space/proj")]
    [InlineData("/already/linux", "/already/linux")]
    [InlineData(@"C:", "/mnt/c/")]
    public void ToWslPath_ConvertsWindowsPaths(string input, string expected)
    {
        Assert.Equal(expected, ToolLauncherService.ToWslPath(input));
    }

    [Fact]
    public void Resolvers_ReturnExistingFileOrNull()
    {
        var wtPath = ToolLauncherService.ResolveWindowsTerminalPath();
        if (wtPath is not null)
        {
            Assert.True(File.Exists(wtPath));
        }

        var codePath = ToolLauncherService.ResolveVsCodeCliPath();
        if (codePath is not null)
        {
            Assert.True(File.Exists(codePath));
        }
    }
}
