using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class ApexServiceTests
{
    private static SfCliResult Raw(string stdout, int exitCode = 0) =>
        new(new[] { "apex", "run", "--json" }, exitCode, stdout, string.Empty, false, TimeSpan.FromSeconds(1));

    [Fact]
    public void ParseRunResult_Success_ParsesFieldsAndLogs()
    {
        var json = """{"status":0,"result":{"success":true,"compiled":true,"compileProblem":"","exceptionMessage":"","exceptionStackTrace":"","line":-1,"column":-1,"logs":"68.0 ... USER_DEBUG|hello"},"warnings":[]}""";

        var result = ApexService.ParseRunResult(Raw(json));

        Assert.True(result.Success);
        Assert.True(result.Compiled);
        Assert.Null(result.CompileProblem);
        Assert.Null(result.ExceptionMessage);
        Assert.Equal("68.0 ... USER_DEBUG|hello", result.Logs);
    }

    [Fact]
    public void ParseRunResult_CompileError_KeepsProblem()
    {
        var json = """{"status":1,"result":{"success":false,"compiled":false,"compileProblem":"Line 1: Unexpected token","exceptionMessage":"","exceptionStackTrace":"","logs":""},"exitCode":1}""";

        var result = ApexService.ParseRunResult(Raw(json, exitCode: 1));

        Assert.False(result.Success);
        Assert.False(result.Compiled);
        Assert.Equal("Line 1: Unexpected token", result.CompileProblem);
    }

    [Fact]
    public void ParseRunResult_HardFailure_UsesCommandMessage()
    {
        var json = """{"name":"Error","message":"No authorization information found.","status":1,"exitCode":1}""";

        var result = ApexService.ParseRunResult(Raw(json, exitCode: 1));

        Assert.False(result.Success);
        Assert.False(result.Compiled);
        Assert.Contains("No authorization", result.ErrorMessage);
    }

    [Theory]
    [InlineData("\"log text\"", "log text")]
    [InlineData("{\"log\":\"log text\"}", "log text")]
    [InlineData("[{\"log\":\"log text\"}]", "log text")]
    public void ExtractLogText_HandlesVariants(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, ApexService.ExtractLogText(document.RootElement));
    }

    [Fact]
    public void ExtractLogText_EmptyArray_ReturnsNull()
    {
        using var document = JsonDocument.Parse("[]");

        Assert.Null(ApexService.ExtractLogText(document.RootElement));
    }

    [Fact]
    public void ParseLogList_ParsesAndSortsDescending()
    {
        var json = """
        [
          { "Id": "07L1", "StartTime": "2026-10-02T10:00:00.000+0000", "Operation": "Anonymous Apex", "Status": "Success", "DurationMilliseconds": 42, "LogLength": 1234 },
          { "Id": "07L2", "StartTime": "2026-10-02T12:00:00.000+0000", "Operation": "API", "Status": "Success", "DurationMilliseconds": 10, "LogLength": 567 }
        ]
        """;
        using var document = JsonDocument.Parse(json);

        var logs = ApexService.ParseLogList(document.RootElement);

        Assert.Equal(2, logs.Count);
        Assert.Equal("07L2", logs[0].Id); // 新しい順
        Assert.Equal(1234, logs.Single(l => l.Id == "07L1").LogLength);
        Assert.Equal(42, logs.Single(l => l.Id == "07L1").DurationMs);
    }
}
