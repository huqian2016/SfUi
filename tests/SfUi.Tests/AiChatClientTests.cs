using System.Net;
using System.Text;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class AiChatClientTests : IDisposable
{
    private readonly List<string> _sandboxes = new();

    public void Dispose()
    {
        foreach (var sandbox in _sandboxes)
        {
            try
            {
                if (Directory.Exists(sandbox))
                {
                    Directory.Delete(sandbox, recursive: true);
                }
            }
            catch
            {
                // 後始末の失敗はテスト結果に影響させない
            }
        }
    }

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
    public void ResolveApiKey_IgnoresLegacyEnvironment_WhenNotAllowed()
    {
        var (key, source) = AiChatClient.ResolveApiKey(null, null, "legacy-key", null, false);

        Assert.Null(key);
        Assert.Equal("none", source);
    }

    [Fact]
    public void ResolveApiKey_UsesProvidedBuiltIn_WhenLegacyNotAllowed()
    {
        // builtIn は呼び出し側（ResolveBuiltInKey）が対象エンドポイントを判定済みの値を渡す
        var (key, source) = AiChatClient.ResolveApiKey(null, null, "legacy-key", "builtin-key", false);

        Assert.Equal("builtin-key", key);
        Assert.Equal("builtin", source);
    }

    [Fact]
    public void ResolveApiKey_DecodesProtectedConfiguredKey()
    {
        var (key, source) = AiChatClient.ResolveApiKey(AiKeyObfuscation.Protect("sk-enc"), null, null, null, false);

        Assert.Equal("sk-enc", key);
        Assert.Equal("settings", source);
    }

    [Fact]
    public void ResolveApiKey_DecodesProtectedEnvironmentKey()
    {
        var (key, source) = AiChatClient.ResolveApiKey(null, AiKeyObfuscation.Protect("sk-env"), null, null, false);

        Assert.Equal("sk-env", key);
        Assert.Equal("env", source);
    }

    [Fact]
    public void ResolveApiKey_SkipsUnreadableProtectedValue_AndFallsBack()
    {
        var (key, source) = AiChatClient.ResolveApiKey("enc1:%%%broken%%%", null, null, "builtin-key", false);

        Assert.Equal("builtin-key", key);
        Assert.Equal("builtin", source);
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

    [Fact]
    public void DefaultOpenAiKey_IsNullOrValidKeyFormat()
    {
        var value = DefaultOpenAiKey.Value;

        if (value is not null)
        {
            Assert.StartsWith("sk-", value);
        }
    }

    [Fact]
    public void ResolveBuiltInKey_ReturnsNull_ForCustomEndpoint()
        => Assert.Null(AiChatClient.ResolveBuiltInKey("http://localhost:11434/v1/chat/completions"));

    [Fact]
    public void ResolveBuiltInKey_ReturnsDeepSeekBuiltIn_ForDeepSeekDefault()
    {
        var key = AiChatClient.ResolveBuiltInKey(AiChatClient.DefaultEndpoint);

        Assert.NotNull(DefaultAiKey.Value);
        Assert.Equal(DefaultAiKey.Value, key);
    }

    [Fact]
    public void ResolveBuiltInKey_ReturnsOpenAiBuiltIn_ForOpenAiDefault()
    {
        // 大文字小文字は区別しない。値は DefaultOpenAiKey と一致（未設定の間は null）
        var key = AiChatClient.ResolveBuiltInKey("HTTPS://API.OPENAI.COM/v1/chat/completions");

        Assert.Equal(DefaultOpenAiKey.Value, key);
    }

    [Theory]
    [InlineData(401, "builtin", true)]
    [InlineData(402, "builtin", true)]
    [InlineData(403, "builtin", true)]
    [InlineData(429, "builtin", true)]
    [InlineData(400, "builtin", false)]
    [InlineData(500, "builtin", false)]
    [InlineData(401, "settings", false)]
    [InlineData(401, "env", false)]
    [InlineData(401, "legacy-env", false)]
    [InlineData(401, "none", false)]
    public void ShouldSuggestOwnKey_Matches(int statusCode, string source, bool expected)
        => Assert.Equal(expected, AiChatClient.ShouldSuggestOwnKey(statusCode, source));

    [Fact]
    public void DescribeError_UsesBuiltInGuidance_OnlyForBuiltInSource()
    {
        var builtIn = AiChatClient.DescribeError(429, "builtin", "quota detail");
        var ownKey = AiChatClient.DescribeError(429, "settings", "quota detail");

        Assert.NotEqual(ownKey, builtIn);
        Assert.Contains("quota detail", builtIn);
        Assert.Contains("quota detail", ownKey);
    }

    // --- パラメータ非対応（HTTP 400）時の調整リトライ ---

    private const string MaxTokensErrorJson =
        """{"error":{"message":"Unsupported parameter: 'max_tokens' is not supported with this model. Use 'max_completion_tokens' instead.","type":"invalid_request_error","param":"max_tokens","code":"unsupported_parameter"}}""";

    private const string TemperatureErrorJson =
        """{"error":{"message":"Unsupported value: 'temperature' does not support 0.3 with this model. Only the default (1) value is supported.","type":"invalid_request_error","param":"temperature","code":"unsupported_value"}}""";

    private const string MaxCompletionTokensErrorJson =
        """{"error":{"message":"Unsupported parameter: 'max_completion_tokens' is not supported with this model.","type":"invalid_request_error","param":"max_completion_tokens","code":"unsupported_parameter"}}""";

    [Fact]
    public void PlanParameterRetry_MapsUnsupportedMaxTokens()
        => Assert.Equal(
            AiChatClient.RetryAction.UseMaxCompletionTokens,
            AiChatClient.PlanParameterRetry(MaxTokensErrorJson, useMaxCompletionTokens: false, includeTokenLimit: true, includeTemperature: true));

    [Fact]
    public void PlanParameterRetry_ReturnsNone_WhenMaxCompletionTokensAlreadyUsed()
        => Assert.Equal(
            AiChatClient.RetryAction.None,
            AiChatClient.PlanParameterRetry(MaxTokensErrorJson, useMaxCompletionTokens: true, includeTokenLimit: true, includeTemperature: true));

    [Fact]
    public void PlanParameterRetry_MapsUnsupportedTemperature()
        => Assert.Equal(
            AiChatClient.RetryAction.OmitTemperature,
            AiChatClient.PlanParameterRetry(TemperatureErrorJson, useMaxCompletionTokens: false, includeTokenLimit: true, includeTemperature: true));

    [Fact]
    public void PlanParameterRetry_ReturnsNone_WhenTemperatureAlreadyOmitted()
        => Assert.Equal(
            AiChatClient.RetryAction.None,
            AiChatClient.PlanParameterRetry(TemperatureErrorJson, useMaxCompletionTokens: false, includeTokenLimit: true, includeTemperature: false));

    [Fact]
    public void PlanParameterRetry_MapsUnsupportedMaxCompletionTokens()
        => Assert.Equal(
            AiChatClient.RetryAction.OmitTokenLimit,
            AiChatClient.PlanParameterRetry(MaxCompletionTokensErrorJson, useMaxCompletionTokens: true, includeTokenLimit: true, includeTemperature: true));

    [Fact]
    public void PlanParameterRetry_ReturnsNone_ForUnrelatedOrMalformedError()
    {
        Assert.Equal(AiChatClient.RetryAction.None, AiChatClient.PlanParameterRetry(ErrorJson, false, true, true));
        Assert.Equal(AiChatClient.RetryAction.None, AiChatClient.PlanParameterRetry("not json", false, true, true));
    }

    [Fact]
    public async Task ChatAsync_SendsStandardParameters_OnFirstAttempt()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, SuccessJson));
        var client = CreateClient(handler);

        var result = await client.ChatAsync(new[] { new AiChatClient.ChatMessage("user", "hi") });

        Assert.True(result.Success);
        var request = Assert.Single(handler.RequestBodies);
        Assert.Contains("\"max_tokens\"", request);
        Assert.Contains("\"temperature\"", request);
    }

    [Fact]
    public async Task ChatAsync_RetriesWithMaxCompletionTokensAndDropsTemperature()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.BadRequest, MaxTokensErrorJson),
            (HttpStatusCode.BadRequest, TemperatureErrorJson),
            (HttpStatusCode.OK, SuccessJson));
        var client = CreateClient(handler);

        var result = await client.ChatAsync(new[] { new AiChatClient.ChatMessage("user", "hi") });

        Assert.True(result.Success);
        Assert.Equal(3, handler.RequestBodies.Count);
        Assert.Contains("\"max_tokens\"", handler.RequestBodies[0]);
        Assert.Contains("\"temperature\"", handler.RequestBodies[0]);
        Assert.Contains("\"max_completion_tokens\"", handler.RequestBodies[1]);
        Assert.DoesNotContain("\"max_tokens\"", handler.RequestBodies[1]);
        Assert.Contains("\"temperature\"", handler.RequestBodies[1]);
        Assert.DoesNotContain("\"temperature\"", handler.RequestBodies[2]);
        Assert.Contains("\"max_completion_tokens\"", handler.RequestBodies[2]);
    }

    [Fact]
    public async Task ChatAsync_DropsOnlyTemperature_WhenTemperatureUnsupported()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.BadRequest, TemperatureErrorJson),
            (HttpStatusCode.OK, SuccessJson));
        var client = CreateClient(handler);

        var result = await client.ChatAsync(new[] { new AiChatClient.ChatMessage("user", "hi") });

        Assert.True(result.Success);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.DoesNotContain("\"temperature\"", handler.RequestBodies[1]);
        Assert.Contains("\"max_tokens\"", handler.RequestBodies[1]);
    }

    [Fact]
    public async Task ChatAsync_DropsTokenLimit_WhenMaxCompletionTokensUnsupported()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.BadRequest, MaxTokensErrorJson),
            (HttpStatusCode.BadRequest, MaxCompletionTokensErrorJson),
            (HttpStatusCode.OK, SuccessJson));
        var client = CreateClient(handler);

        var result = await client.ChatAsync(new[] { new AiChatClient.ChatMessage("user", "hi") });

        Assert.True(result.Success);
        Assert.Equal(3, handler.RequestBodies.Count);
        Assert.DoesNotContain("\"max_tokens\"", handler.RequestBodies[2]);
        Assert.DoesNotContain("\"max_completion_tokens\"", handler.RequestBodies[2]);
        Assert.Contains("\"temperature\"", handler.RequestBodies[2]);
    }

    [Fact]
    public async Task ChatAsync_DoesNotRetry_OnUnrelatedError()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key provided","type":"invalid_request_error","code":"invalid_api_key"}}"""));
        var client = CreateClient(handler);

        var result = await client.ChatAsync(new[] { new AiChatClient.ChatMessage("user", "hi") });

        Assert.False(result.Success);
        Assert.Single(handler.RequestBodies);
        Assert.Contains("Incorrect API key provided", result.Error);
    }

    private AiChatClient CreateClient(ScriptedHandler handler)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        _sandboxes.Add(sandbox);
        var paths = AppPaths.Resolve(dataRootOverride: sandbox, baseDirectory: sandbox, appDataDirectory: sandbox);
        var log = new AppLog(paths);
        var store = new AppSettingsStore(paths, log);
        store.Current.AiEndpoint = "https://api.openai.com/v1/chat/completions";
        store.Current.AiApiKey = "sk-test";
        store.Current.AiModel = "gpt-6-luna";
        return new AiChatClient(store, log, handler);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses;

        public ScriptedHandler(params (HttpStatusCode Status, string Body)[] responses)
            => _responses = new Queue<(HttpStatusCode, string)>(responses);

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            var (status, body) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.OK, SuccessJson);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
