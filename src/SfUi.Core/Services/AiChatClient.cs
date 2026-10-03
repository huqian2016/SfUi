using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// OpenAI 互換チャット補完 API のクライアント。
/// 既定は DeepSeek（内蔵の評価用キーでそのまま動作）。設定（AiEndpoint / AiApiKey / AiModel）で
/// OpenAI 互換サービス（OpenAI・OpenRouter・Anthropic 互換レイヤー・社内ゲートウェイ等）や
/// ローカル LLM（Ollama / LM Studio）へ切り替えられる。
/// キーは 設定 → 環境変数 SFUI_AI_API_KEY → 旧環境変数 DEEPSEEK_API_KEY / 内蔵キー（後者 2 つは DeepSeek 既定接続先のみ）
/// の順で解決する。カスタム接続先ではキー未設定でも送信する（ローカル LLM 用）。
/// </summary>
public sealed class AiChatClient
{
    public const string DefaultModel = "deepseek-chat";
    public const string DefaultEndpoint = "https://api.deepseek.com/chat/completions";

    /// <summary>汎用の API キー環境変数（すべての接続先で使用可）。</summary>
    public const string ApiKeyEnvironmentVariable = "SFUI_AI_API_KEY";

    /// <summary>旧名の API キー環境変数（DeepSeek 既定接続先のときのみ使用）。</summary>
    public const string LegacyApiKeyEnvironmentVariable = "DEEPSEEK_API_KEY";

    private readonly AppSettingsStore _settings;
    private readonly AppLog _log;
    private readonly HttpClient _http;

    public AiChatClient(AppSettingsStore settings, AppLog log)
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

    /// <summary>現在の接続先エンドポイント（設定 → DeepSeek 既定）。</summary>
    public string Endpoint => ResolveEndpoint(_settings.Current.AiEndpoint);

    /// <summary>接続先が DeepSeek 既定かどうか（旧環境変数・内蔵キーを使う条件）。</summary>
    public bool IsDefaultEndpoint => string.Equals(Endpoint, DefaultEndpoint, StringComparison.OrdinalIgnoreCase);

    /// <summary>現在有効な API キー（設定 → 汎用環境変数 → 旧環境変数/内蔵キー〔既定接続先のみ〕）。</summary>
    public string? ApiKey => ResolveApiKey(
        _settings.Current.AiApiKey,
        Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable),
        Environment.GetEnvironmentVariable(LegacyApiKeyEnvironmentVariable),
        DefaultAiKey.Value,
        IsDefaultEndpoint).Key;

    /// <summary>現在のキー取得元（settings / env / legacy-env / builtin / none）。</summary>
    public string ApiKeySource => ResolveApiKey(
        _settings.Current.AiApiKey,
        Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable),
        Environment.GetEnvironmentVariable(LegacyApiKeyEnvironmentVariable),
        DefaultAiKey.Value,
        IsDefaultEndpoint).Source;

    /// <summary>現在有効なモデル名。</summary>
    public string Model => ResolveModel(_settings.Current.AiModel);

    /// <summary>接続先（null / 空 = DeepSeek 既定）。</summary>
    public static string ResolveEndpoint(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultEndpoint : configured.Trim();

    /// <summary>モデル名（null / 空 = 既定: deepseek-chat）。</summary>
    public static string ResolveModel(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultModel : configured.Trim();

    /// <summary>
    /// API キーと取得元を解決する（単体テスト対象）。旧環境変数と内蔵キーは DeepSeek 既定接続先でのみ使用する
    /// （カスタム接続先へ DeepSeek のキーを送らないため）。
    /// </summary>
    public static (string? Key, string Source) ResolveApiKey(
        string? configured,
        string? genericEnvironment,
        string? legacyEnvironment,
        string? builtIn,
        bool isDefaultEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return (configured.Trim(), "settings");
        }

        if (!string.IsNullOrWhiteSpace(genericEnvironment))
        {
            return (genericEnvironment.Trim(), "env");
        }

        if (isDefaultEndpoint)
        {
            if (!string.IsNullOrWhiteSpace(legacyEnvironment))
            {
                return (legacyEnvironment.Trim(), "legacy-env");
            }

            if (!string.IsNullOrWhiteSpace(builtIn))
            {
                return (builtIn.Trim(), "builtin");
            }
        }

        return (null, "none");
    }

    /// <summary>チャット補完を実行する（ストリーミングなし）。</summary>
    public async Task<ChatResult> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ApiKey;
        if (apiKey is null && IsDefaultEndpoint)
        {
            // DeepSeek 既定接続先ではキー必須（内蔵キーが除去された場合のみここに来る）
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
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            if (apiKey is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var error = ExtractError(body) ?? $"HTTP {(int)response.StatusCode}";
                _log.Warn($"AI API エラー: HTTP {(int)response.StatusCode} / {error}");
                return new ChatResult(false, null, UiText.T("Ai_ServerErrorFmt", error), null, null, stopwatch.Elapsed);
            }

            var content = ExtractContent(body);
            var (promptTokens, completionTokens) = ExtractUsage(body);
            if (content is null)
            {
                return new ChatResult(false, null, UiText.T("Ai_ServerErrorFmt", "unexpected response"), promptTokens, completionTokens, stopwatch.Elapsed);
            }

            _log.Info($"AI 応答受信: {stopwatch.Elapsed.TotalSeconds:F1} 秒 / tokens {promptTokens}+{completionTokens}");
            return new ChatResult(true, content, null, promptTokens, completionTokens, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _log.Warn("AI リクエストがタイムアウトしました");
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
            _log.Error("AI リクエストに失敗", ex);
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
