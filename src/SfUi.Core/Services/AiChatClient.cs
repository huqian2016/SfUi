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
/// モデルが対応しないパラメータ（max_tokens / temperature 等）は HTTP 400 を検知して調整し再試行する。
/// </summary>
public sealed class AiChatClient
{
    public const string DefaultModel = "deepseek-chat";
    public const string DefaultEndpoint = "https://api.deepseek.com/chat/completions";

    /// <summary>OpenAI の既定エンドポイント（内蔵キーの適用対象）。</summary>
    public const string OpenAiDefaultEndpoint = "https://api.openai.com/v1/chat/completions";

    /// <summary>汎用の API キー環境変数（すべての接続先で使用可）。</summary>
    public const string ApiKeyEnvironmentVariable = "SFUI_AI_API_KEY";

    /// <summary>旧名の API キー環境変数（DeepSeek 既定接続先のときのみ使用）。</summary>
    public const string LegacyApiKeyEnvironmentVariable = "DEEPSEEK_API_KEY";

    /// <summary>パラメータ非対応（HTTP 400）時の最大再試行回数。</summary>
    private const int MaxParameterRetries = 3;

    private readonly AppSettingsStore _settings;
    private readonly AppLog _log;
    private readonly HttpClient _http;

    public AiChatClient(AppSettingsStore settings, AppLog log)
        : this(settings, log, new HttpClientHandler())
    {
    }

    /// <summary>テスト用: 任意の HttpMessageHandler を注入して生成する。</summary>
    internal AiChatClient(AppSettingsStore settings, AppLog log, HttpMessageHandler handler)
    {
        _settings = settings;
        _log = log;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
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

    /// <summary>接続先が DeepSeek 既定かどうか（旧環境変数を使う条件）。</summary>
    public bool IsDefaultEndpoint => string.Equals(Endpoint, DefaultEndpoint, StringComparison.OrdinalIgnoreCase);

    /// <summary>接続先が OpenAI の既定エンドポイントかどうか。</summary>
    public bool IsOpenAiDefaultEndpoint => string.Equals(Endpoint, OpenAiDefaultEndpoint, StringComparison.OrdinalIgnoreCase);

    /// <summary>API キーが必須の既定エンドポイントかどうか（キー未設定なら送信前に案内する）。</summary>
    public bool RequiresApiKey => IsDefaultEndpoint || IsOpenAiDefaultEndpoint;

    /// <summary>現在の接続先に対応する内蔵キー（既定エンドポイント以外は null = 送信しない）。</summary>
    public string? BuiltInKey => ResolveBuiltInKey(Endpoint);

    /// <summary>接続先に対応する内蔵キーを解決する（既定エンドポイントのみ。カスタム接続先へ内蔵キーを送らないための判定）。</summary>
    public static string? ResolveBuiltInKey(string endpoint)
    {
        if (string.Equals(endpoint, DefaultEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultAiKey.Value;
        }

        if (string.Equals(endpoint, OpenAiDefaultEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultOpenAiKey.Value;
        }

        return null;
    }

    /// <summary>現在有効な API キー（設定 → 汎用環境変数 → 旧環境変数/内蔵キー〔既定接続先のみ〕。enc1: は復元）。</summary>
    public string? ApiKey => ResolveApiKey(
        _settings.Current.AiApiKey,
        Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable),
        Environment.GetEnvironmentVariable(LegacyApiKeyEnvironmentVariable),
        BuiltInKey,
        IsDefaultEndpoint).Key;

    /// <summary>現在のキー取得元（settings / env / legacy-env / builtin / none）。</summary>
    public string ApiKeySource => ResolveApiKey(
        _settings.Current.AiApiKey,
        Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable),
        Environment.GetEnvironmentVariable(LegacyApiKeyEnvironmentVariable),
        BuiltInKey,
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
    /// API キーと取得元を解決する（単体テスト対象）。旧環境変数は DeepSeek 既定接続先でのみ使用する
    /// （カスタム接続先へ DeepSeek のキーを送らないため）。内蔵キーは呼び出し側（ResolveBuiltInKey）が
    /// 対象エンドポイントを判定済みのものを渡す。なお値は enc1: 形式（難読化）なら復元し、平文はそのまま使用する。
    /// </summary>
    public static (string? Key, string Source) ResolveApiKey(
        string? configured,
        string? genericEnvironment,
        string? legacyEnvironment,
        string? builtIn,
        bool allowLegacyEnvironment)
    {
        var configuredKey = AiKeyObfuscation.Normalize(configured);
        if (configuredKey is not null)
        {
            return (configuredKey, "settings");
        }

        var genericKey = AiKeyObfuscation.Normalize(genericEnvironment);
        if (genericKey is not null)
        {
            return (genericKey, "env");
        }

        if (allowLegacyEnvironment)
        {
            var legacyKey = AiKeyObfuscation.Normalize(legacyEnvironment);
            if (legacyKey is not null)
            {
                return (legacyKey, "legacy-env");
            }
        }

        var builtInKey = AiKeyObfuscation.Normalize(builtIn);
        if (builtInKey is not null)
        {
            return (builtInKey, "builtin");
        }

        return (null, "none");
    }

    /// <summary>
    /// チャット補完を実行する（ストリーミングなし）。
    /// プロバイダ・モデルが対応しないパラメータ（max_tokens / temperature / max_completion_tokens）は
    /// 400 応答を検知して調整のうえ再試行する。
    /// </summary>
    public async Task<ChatResult> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ApiKey;
        var apiKeySource = ApiKeySource;
        if (apiKey is null && RequiresApiKey)
        {
            // 既定接続先ではキー必須（内蔵キーが除去・未設定の場合のみここに来る）
            return new ChatResult(false, null, UiText.T("Ai_NoApiKey"), null, null, TimeSpan.Zero);
        }

        // パラメータの受け付け可否はプロバイダ・モデルごとに異なる
        // （例: OpenAI の新しいモデルは max_tokens 非対応・temperature は既定値のみ）。
        // 400（unsupported_parameter / unsupported_value）を受けたら該当パラメータを調整して再試行する。
        var useMaxCompletionTokens = false;
        var includeTokenLimit = true;
        var includeTemperature = true;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var payload = BuildPayload(Model, messages, includeTemperature, includeTokenLimit, useMaxCompletionTokens);

                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
                if (apiKey is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }

                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    stopwatch.Stop();
                    var content = ExtractContent(body);
                    var (promptTokens, completionTokens) = ExtractUsage(body);
                    if (content is null)
                    {
                        return new ChatResult(false, null, UiText.T("Ai_ServerErrorFmt", "unexpected response"), promptTokens, completionTokens, stopwatch.Elapsed);
                    }

                    _log.Info($"AI 応答受信: {stopwatch.Elapsed.TotalSeconds:F1} 秒 / tokens {promptTokens}+{completionTokens}");
                    return new ChatResult(true, content, null, promptTokens, completionTokens, stopwatch.Elapsed);
                }

                var error = ExtractError(body) ?? $"HTTP {(int)response.StatusCode}";
                var retry = attempt < MaxParameterRetries
                    ? PlanParameterRetry(body, useMaxCompletionTokens, includeTokenLimit, includeTemperature)
                    : RetryAction.None;

                if (retry == RetryAction.None)
                {
                    stopwatch.Stop();
                    var statusCode = (int)response.StatusCode;
                    _log.Warn($"AI API エラー: HTTP {statusCode} / {error}");
                    if (ShouldSuggestOwnKey(statusCode, apiKeySource))
                    {
                        _log.Warn("AI API: 内蔵キーが利用できないため、自分のキー登録を案内します");
                    }

                    return new ChatResult(false, null, DescribeError(statusCode, apiKeySource, error), null, null, stopwatch.Elapsed);
                }

                switch (retry)
                {
                    case RetryAction.UseMaxCompletionTokens:
                        useMaxCompletionTokens = true;
                        _log.Warn("AI API: 'max_tokens' が非対応のため 'max_completion_tokens' で再試行します");
                        break;
                    case RetryAction.OmitTemperature:
                        includeTemperature = false;
                        _log.Warn("AI API: 'temperature' が非対応のため既定値で再試行します");
                        break;
                    case RetryAction.OmitTokenLimit:
                        includeTokenLimit = false;
                        _log.Warn("AI API: 'max_completion_tokens' が非対応のためトークン上限なしで再試行します");
                        break;
                }
            }
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

    /// <summary>400 応答（パラメータ非対応）を受けたときの再試行アクション。</summary>
    public enum RetryAction
    {
        /// <summary>再試行しない。</summary>
        None,

        /// <summary>max_tokens → max_completion_tokens へ置き換える。</summary>
        UseMaxCompletionTokens,

        /// <summary>temperature を送らない（既定値を使う）。</summary>
        OmitTemperature,

        /// <summary>トークン上限を送らない。</summary>
        OmitTokenLimit,
    }

    /// <summary>
    /// HTTP 400 のエラー応答（OpenAI の unsupported_parameter / unsupported_value）から、
    /// 再試行で調整すべきパラメータを判定する（調整不要・判定不能なら None）。
    /// </summary>
    public static RetryAction PlanParameterRetry(
        string errorJson,
        bool useMaxCompletionTokens,
        bool includeTokenLimit,
        bool includeTemperature)
    {
        try
        {
            using var document = JsonDocument.Parse(errorJson);
            if (!document.RootElement.TryGetProperty("error", out var error))
            {
                return RetryAction.None;
            }

            var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var param = error.TryGetProperty("param", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (code is not ("unsupported_parameter" or "unsupported_value") || param is null)
            {
                return RetryAction.None;
            }

            return param switch
            {
                "max_tokens" when !useMaxCompletionTokens => RetryAction.UseMaxCompletionTokens,
                "max_completion_tokens" when includeTokenLimit => RetryAction.OmitTokenLimit,
                "temperature" when includeTemperature => RetryAction.OmitTemperature,
                _ => RetryAction.None,
            };
        }
        catch (JsonException)
        {
            return RetryAction.None;
        }
    }

    /// <summary>リクエスト JSON を構築する（temperature 0.3 / トークン上限 4096 が既定）。</summary>
    private static Dictionary<string, object?> BuildPayload(
        string model,
        IReadOnlyList<ChatMessage> messages,
        bool includeTemperature,
        bool includeTokenLimit,
        bool useMaxCompletionTokens)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            ["stream"] = false,
        };

        if (includeTemperature)
        {
            payload["temperature"] = 0.3;
        }

        if (includeTokenLimit)
        {
            payload[useMaxCompletionTokens ? "max_completion_tokens" : "max_tokens"] = 4096;
        }

        return payload;
    }

    /// <summary>内蔵キーの上限到達・失効が疑われる応答かどうか（自分のキー登録を案内する条件）。</summary>
    public static bool ShouldSuggestOwnKey(int statusCode, string? apiKeySource) =>
        string.Equals(apiKeySource, "builtin", StringComparison.OrdinalIgnoreCase)
        && statusCode is 401 or 402 or 403 or 429;

    /// <summary>エラー応答を表示用メッセージへ変換する（内蔵キーの認証・残高・レート系エラー時は自分のキー登録を案内）。</summary>
    public static string DescribeError(int statusCode, string? apiKeySource, string error) =>
        ShouldSuggestOwnKey(statusCode, apiKeySource)
            ? UiText.T("Ai_BuiltInKeyUnavailableFmt", error)
            : UiText.T("Ai_ServerErrorFmt", error);

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
