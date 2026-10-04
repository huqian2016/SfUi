using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>PATH 走査（OS 別の区切り文字・ファイル名）と PlatformInfo のテスト。</summary>
public class PlatformSearchTests
{
    [Fact]
    public void Find_UsesPosixSeparatorAndFileName()
    {
        var expected = Path.Combine("/opt/homebrew/bin", "sf");
        var found = PathSearch.Find(
            "/usr/bin:/opt/homebrew/bin:/usr/local/bin",
            new[] { "sf" },
            ':',
            path => path == expected);

        Assert.Equal(expected, found);
    }

    [Fact]
    public void Find_UsesWindowsSeparatorAndExtensions()
    {
        var expected = Path.Combine(@"C:\sf\bin", "sf.exe");
        var found = PathSearch.Find(
            @"C:\tools;C:\sf\bin",
            new[] { "sf.cmd", "sf.exe" },
            ';',
            path => path == expected);

        Assert.Equal(expected, found);
    }

    [Fact]
    public void Find_ReturnsNull_WhenNothingMatches()
    {
        Assert.Null(PathSearch.Find("/a:/b", new[] { "sf" }, ':', _ => false));
        Assert.Null(PathSearch.Find(null, new[] { "sf" }, ':'));
        Assert.Null(PathSearch.Find("   ", new[] { "sf" }, ':'));
    }

    [Fact]
    public void Find_IgnoresQuotedEntries()
    {
        var expected = Path.Combine("/opt/bin", "sf");
        var found = PathSearch.Find("\"/opt/bin\"", new[] { "sf" }, ':', path => path == expected);

        Assert.Equal(expected, found);
    }

    [Fact]
    public void PlatformInfo_MatchesRuntimeOs()
    {
        Assert.Equal(OperatingSystem.IsWindows(), PlatformInfo.IsWindows);
        Assert.Equal(OperatingSystem.IsMacOS(), PlatformInfo.IsMacOS);
        Assert.Equal(PlatformInfo.IsWindows ? ';' : ':', PlatformInfo.PathListSeparator);
    }
}
