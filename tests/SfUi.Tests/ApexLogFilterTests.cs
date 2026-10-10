using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>匿名 Apex の System.debug のみ表示フィルタのテスト。</summary>
public class ApexLogFilterTests
{
    [Fact]
    public void FilterUserDebugLines_KeepsOnlyUserDebugLines()
    {
        var log = string.Join('\n', new[]
        {
            "13:00:00.0 (1)|EXECUTION_STARTED",
            "13:00:00.1 (2)|USER_DEBUG|[1]|DEBUG|Hello SfUi",
            "13:00:00.2 (3)|SOQL_EXECUTE_BEGIN|[2]|Aggregations:0|SELECT Id FROM Account",
            "13:00:00.3 (4)|USER_DEBUG|[3]|DEBUG|second line",
            "13:00:00.4 (5)|EXECUTION_FINISHED",
        });
        var filtered = ApexLogFilter.FilterUserDebugLines(log);
        var lines = filtered.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("Hello SfUi", lines[0]);
        Assert.Contains("second line", lines[1]);
        Assert.DoesNotContain("EXECUTION_STARTED", filtered);
        Assert.DoesNotContain("SOQL_EXECUTE_BEGIN", filtered);
    }

    [Fact]
    public void FilterUserDebugLines_HandlesCrLfAndTrailingNewline()
    {
        var log = "a|USER_DEBUG|[1]|DEBUG|one\r\nb|USER_DEBUG|[2]|DEBUG|two\r\n";
        var filtered = ApexLogFilter.FilterUserDebugLines(log);
        Assert.Equal("a|USER_DEBUG|[1]|DEBUG|one\nb|USER_DEBUG|[2]|DEBUG|two", filtered);
    }

    [Fact]
    public void FilterUserDebugLines_ReturnsEmptyWhenNoDebugOutput()
    {
        Assert.Equal(string.Empty, ApexLogFilter.FilterUserDebugLines(null));
        Assert.Equal(string.Empty, ApexLogFilter.FilterUserDebugLines(string.Empty));
        Assert.Equal(string.Empty, ApexLogFilter.FilterUserDebugLines("13:00:00.0 (1)|EXECUTION_STARTED"));
    }
}
