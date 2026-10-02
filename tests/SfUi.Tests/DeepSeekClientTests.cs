using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class DeepSeekClientTests
{
    private const string SuccessJson =
        """{"id":"x","choices":[{"index":0,"message":{"role":"assistant","content":"SELECT Id FROM Account LIMIT 1"},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":34,"total_tokens":46}}""";

    private const string ErrorJson =
        """{"error":{"message":"Insufficient Balance","type":"insufficient_quota"}}""";

    [Fact]
    public void ExtractContent_ReturnsAssistantContent()
        => Assert.Equal("SELECT Id FROM Account LIMIT 1", DeepSeekClient.ExtractContent(SuccessJson));

    [Fact]
    public void ExtractContent_ReturnsNull_WhenMalformed()
        => Assert.Null(DeepSeekClient.ExtractContent("not json"));

    [Fact]
    public void ExtractContent_ReturnsNull_WhenNoChoices()
        => Assert.Null(DeepSeekClient.ExtractContent("{}"));

    [Fact]
    public void ExtractError_ReturnsErrorMessage()
        => Assert.Equal("Insufficient Balance", DeepSeekClient.ExtractError(ErrorJson));

    [Fact]
    public void ExtractError_ReturnsNull_WhenMalformed()
        => Assert.Null(DeepSeekClient.ExtractError("not json"));

    [Fact]
    public void ExtractUsage_ReturnsTokenCounts()
    {
        var (prompt, completion) = DeepSeekClient.ExtractUsage(SuccessJson);

        Assert.Equal(12, prompt);
        Assert.Equal(34, completion);
    }

    [Fact]
    public void ExtractUsage_ReturnsNulls_WhenMissing()
    {
        var (prompt, completion) = DeepSeekClient.ExtractUsage("{}");

        Assert.Null(prompt);
        Assert.Null(completion);
    }

    [Fact]
    public void ResolveApiKey_PrefersConfiguredKey()
        => Assert.Equal("sk-test", DeepSeekClient.ResolveApiKey("  sk-test  "));

    [Fact]
    public void ResolveApiKey_FallsBackToEnvironmentVariable()
        => Assert.Equal(
            Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"),
            DeepSeekClient.ResolveApiKey(null));
}
