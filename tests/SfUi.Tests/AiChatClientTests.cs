using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class AiChatClientTests
{
    private const string SuccessJson =
        """{"id":"x","choices":[{"index":0,"message":{"role":"assistant","content":"SELECT Id FROM Account LIMIT 1"},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":34,"total_tokens":46}}""";

    private const string ErrorJson =
        """{"error":{"message":"Insufficient Balance","type":"insufficient_quota"}}""";

    [Fact]
    public void ExtractContent_ReturnsAssistantContent()
        => Assert.Equal("SELECT Id FROM Account LIMIT 1", AiChatClient.ExtractContent(SuccessJson));

    [Fact]
    public void ExtractContent_ReturnsNull_WhenMalformed()
        => Assert.Null(AiChatClient.ExtractContent("not json"));

    [Fact]
    public void ExtractContent_ReturnsNull_WhenNoChoices()
        => Assert.Null(AiChatClient.ExtractContent("{}"));

    [Fact]
    public void ExtractError_ReturnsErrorMessage()
        => Assert.Equal("Insufficient Balance", AiChatClient.ExtractError(ErrorJson));

    [Fact]
    public void ExtractError_ReturnsNull_WhenMalformed()
        => Assert.Null(AiChatClient.ExtractError("not json"));

    [Fact]
    public void ExtractUsage_ReturnsTokenCounts()
    {
        var (prompt, completion) = AiChatClient.ExtractUsage(SuccessJson);

        Assert.Equal(12, prompt);
        Assert.Equal(34, completion);
    }

    [Fact]
    public void ExtractUsage_ReturnsNulls_WhenMissing()
    {
        var (prompt, completion) = AiChatClient.ExtractUsage("{}");

        Assert.Null(prompt);
        Assert.Null(completion);
    }

    [Fact]
    public void ResolveEndpoint_ReturnsDefault_WhenNullOrBlank()
    {
        Assert.Equal(AiChatClient.DefaultEndpoint, AiChatClient.ResolveEndpoint(null));
        Assert.Equal(AiChatClient.DefaultEndpoint, AiChatClient.ResolveEndpoint("  "));
    }

    [Fact]
    public void ResolveEndpoint_TrimsConfiguredValue()
        => Assert.Equal("http://localhost:11434/v1/chat/completions", AiChatClient.ResolveEndpoint("  http://localhost:11434/v1/chat/completions  "));

    [Fact]
    public void ResolveModel_ReturnsDefault_WhenNullOrBlank()
    {
        Assert.Equal("deepseek-chat", AiChatClient.ResolveModel(null));
        Assert.Equal("deepseek-chat", AiChatClient.ResolveModel("  "));
    }

    [Fact]
    public void ResolveModel_TrimsConfiguredValue()
        => Assert.Equal("gpt-4o-mini", AiChatClient.ResolveModel("  gpt-4o-mini  "));

    [Fact]
    public void ResolveApiKey_PrefersConfiguredKey()
    {
        var (key, source) = AiChatClient.ResolveApiKey("  sk-test  ", null, null, null, true);

        Assert.Equal("sk-test", key);
        Assert.Equal("settings", source);
    }

    [Fact]
    public void ResolveApiKey_PrefersGenericEnvironmentOverLegacyAndBuiltIn()
    {
        var (key, source) = AiChatClient.ResolveApiKey(null, "  env-key  ", "legacy-key", "builtin-key", true);

        Assert.Equal("env-key", key);
        Assert.Equal("env", source);
    }

    [Fact]
    public void ResolveApiKey_UsesLegacyEnvironment_OnDefaultEndpoint()
    {
        var (key, source) = AiChatClient.ResolveApiKey(null, null, "legacy-key", "builtin-key", true);

        Assert.Equal("legacy-key", key);
        Assert.Equal("legacy-env", source);
    }

    [Fact]
    public void ResolveApiKey_UsesBuiltIn_OnDefaultEndpoint()
    {
        var (key, source) = AiChatClient.ResolveApiKey("  ", null, null, "builtin-key", true);

        Assert.Equal("builtin-key", key);
        Assert.Equal("builtin", source);
    }

    [Fact]
    public void ResolveApiKey_IgnoresLegacyAndBuiltIn_OnCustomEndpoint()
    {
        var (key, source) = AiChatClient.ResolveApiKey(null, null, "legacy-key", "builtin-key", false);

        Assert.Null(key);
        Assert.Equal("none", source);
    }

    [Fact]
    public void ResolveApiKey_ReturnsNone_WhenNothingAvailable()
    {
        var (key, source) = AiChatClient.ResolveApiKey(null, null, null, null, true);

        Assert.Null(key);
        Assert.Equal("none", source);
    }

    [Fact]
    public void DefaultAiKey_DecodesToApiKeyFormat()
    {
        var value = DefaultAiKey.Value;

        Assert.NotNull(value);
        Assert.StartsWith("sk-", value);
        Assert.True(value!.Length >= 32);
    }
}
