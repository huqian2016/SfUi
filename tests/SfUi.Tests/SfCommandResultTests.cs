using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class SfCommandResultTests
{
    private static SfCliResult Raw(string stdout, int exitCode = 0, bool timedOut = false, string stderr = "")
        => new(new[] { "org", "list", "--json" }, exitCode, stdout, stderr, timedOut, TimeSpan.FromSeconds(1));

    [Fact]
    public void From_ParsesSuccessfulResult()
    {
        var result = SfCommandResult.From(Raw("""{"status":0,"result":{"totalSize":2}}"""));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Result);
        Assert.Equal(2, result.Result!.Value.GetProperty("totalSize").GetInt32());
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void From_ParsesErrorObject()
    {
        var json = """{"name":"NamedOrgNotFoundError","message":"No authorization information found for no-such-org.","exitCode":1,"status":1}""";

        var result = SfCommandResult.From(Raw(json, exitCode: 1));

        Assert.False(result.IsSuccess);
        Assert.Equal("NamedOrgNotFoundError", result.ErrorName);
        Assert.Contains("No authorization", result.ErrorMessage);
        Assert.Null(result.Result);
    }

    [Fact]
    public void From_HandlesNonJsonOutput()
    {
        var result = SfCommandResult.From(Raw(string.Empty, exitCode: 1, stderr: "boom"));

        Assert.False(result.IsSuccess);
        Assert.Contains("boom", result.ErrorMessage);
    }

    [Fact]
    public void From_MarksTimeoutAsFailure()
    {
        var result = SfCommandResult.From(Raw(string.Empty, exitCode: -1, timedOut: true));

        Assert.False(result.IsSuccess);
        Assert.Equal(UiText.T("Core_Timeout"), result.ErrorMessage);
    }

    [Fact]
    public void From_IgnoresLeadingNoiseBeforeJson()
    {
        var result = SfCommandResult.From(Raw("Warning: something\n{\"status\":0,\"result\":{\"ok\":true}}"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Result!.Value.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void GetResultString_SupportsStringResultAndObjectProperty()
    {
        var stringResult = SfCommandResult.From(Raw("""{"status":0,"result":"00Dxx!token"}"""));
        Assert.Equal("00Dxx!token", stringResult.GetResultString("accessToken"));

        var objectResult = SfCommandResult.From(Raw("""{"status":0,"result":{"accessToken":"abc","apiVersion":"67.0"}}"""));
        Assert.Equal("abc", objectResult.GetResultString("accessToken"));
        Assert.Equal("67.0", objectResult.GetResultString("apiVersion"));
    }

    [Fact]
    public void From_KeepsResultOnFailure()
    {
        var result = SfCommandResult.From(Raw("""{"status":1,"result":{"compiled":false,"compileProblem":"bad"},"exitCode":1}""", exitCode: 1));

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Result);
        Assert.False(result.Result!.Value.GetProperty("compiled").GetBoolean());
    }
}
