using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// DeepSeek API（OpenAI 互換）のチャットクライアント。
/// API キーは settings.json（DeepSeekApiKey）→ 環境変数 DEEPSEEK_API_KEY の順で解決する。
/// </summary>
public sealed class DeepSeekClient
{
    public const string DefaultModel = "deepseek-chat";
    public const string ApiEndpoint = "https://api.deepseek.com/chat/completions";

    private readonly AppSettingsStore _settings;
    private readonly AppLog _log;
    private readonly HttpClient _http;

    public DeepSeekClient(AppSettingsStore settings, AppLog log)
    {
        _settings = settings;
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    /// <summary>チャット 1 通分のメッセージ（role: system / user / assistant）。</summary>
    public sealed record ChatMessage(string Role, string Content);

    /// <summary>チャット実行結果。</summary>
    public sealed record ChatResult(
        bool Success,
        string? Content,
        string? Error,
        int? PromptTokens,
        int? CompletionTokens,
        TimeSpan Duration);

    /// <summary>現在有効な API キー（設定 → 環境変数 → 内蔵キー）。</summary>
    public string? ApiKey => ResolveApiKey(_settings.Current.DeepSeekApiKey);

    /// <summary>現在のキー取得元（settings / env / builtin / none）。</summary>
    public string ApiKeySource
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_settings.Current.DeepSeekApiKey))
            {
                return "settings";
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")))
            {
                return "env";
            }

            return string.IsNullOrWhiteSpace(DefaultAiKey.Value) ? "none" : "builtin";
        }
    }

    /// <summary>現在有効なモデル名。</summary>
    public string Model => string.IsNullOrWhiteSpace(_settings.Current.DeepSeekModel)
        ? DefaultModel
        : _settings.Current.DeepSeekModel.Trim();

    /// <summary>設定値 → 環境変数 → 内蔵キーの順で API キーを解決する。</summary>
    public static string? ResolveApiKey(string? configured)
        => ResolveApiKey(configured, Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"), DefaultAiKey.Value);

    /// <summary>優先順位を明示指定できる版（テスト用）。</summary>
    public static string? ResolveApiKey(string? configured, string? environmentValue, string? builtIn)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
        }

        return string.IsNullOrWhiteSpace(builtIn) ? null : builtIn.Trim();
    }

    /// <summary>チャット補完を実行する（ストリーミングなし）。</summary>
    public async Task<ChatResult> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ApiKey;
        if (apiKey is null)
        {
            return new ChatResult(false, null, UiText.T("Ai_NoApiKey"), null, null, TimeSpan.Zero);
        }

        var payload = new
        {
            model = Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature = 0.3,
            max_tokens = 4096,
            stream = false,
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var error = ExtractError(body) ?? $"HTTP {(int)response.StatusCode}";
                _log.Warn($"DeepSeek API エラー: HTTP {(int)response.StatusCode} / {error}");
                return new ChatResult(false, null, UiText.T("Ai_ServerErrorFmt", error), null, null, stopwatch.Elapsed);
            }

            var content = ExtractContent(body);
            var (promptTokens, completionTokens) = ExtractUsage(body);
            if (content is null)
            {
                return new ChatResult(false, null, UiText.T("Ai_ServerErrorFmt", "unexpected response"), promptTokens, completionTokens, stopwatch.Elapsed);
            }

            _log.Info($"DeepSeek 応答受信: {stopwatch.Elapsed.TotalSeconds:F1} 秒 / tokens {promptTokens}+{completionTokens}");
            return new ChatResult(true, content, null, promptTokens, completionTokens, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _log.Warn("DeepSeek リクエストがタイムアウトしました");
            return new ChatResult(false, null, UiText.T("Ai_Timeout"), null, null, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            // ユーザーによるキャンセルは呼び出し側で処理する
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _log.Error("DeepSeek リクエストに失敗", ex);
            return new ChatResult(false, null, UiText.T("Ai_RequestFailedFmt", ex.Message), null, null, stopwatch.Elapsed);
        }
    }

    /// <summary>応答 JSON から choices[0].message.content を取り出す。</summary>
    public static string? ExtractContent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }
        }
        catch (JsonException)
        {
            // 解析できない応答は null
        }

        return null;
    }

    /// <summary>エラー応答 JSON から error.message を取り出す。</summary>
    public static string? ExtractError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // 解析できない応答は null
        }

        return null;
    }

    /// <summary>usage からトークン数を取り出す。</summary>
    public static (int? Prompt, int? Completion) ExtractUsage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("usage", out var usage))
            {
                int? prompt = usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pv) ? pv : null;
                int? completion = usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var cv) ? cv : null;
                return (prompt, completion);
            }
        }
        catch (JsonException)
        {
            // 解析できない応答は null
        }

        return (null, null);
    }
}
