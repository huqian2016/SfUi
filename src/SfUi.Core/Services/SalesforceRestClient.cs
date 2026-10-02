using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>Salesforce REST API 呼び出しの失敗を表す例外。</summary>
public sealed class SalesforceApiException : Exception
{
    public string? ErrorCode { get; }

    public HttpStatusCode? StatusCode { get; }

    public SalesforceApiException(string message, string? errorCode = null, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}

/// <summary>
/// アクセストークン（OrgService 経由で取得）を使って Salesforce REST / Tooling API を呼び出す。
/// 401 時はトークンを破棄して 1 回だけ再取得・再試行する。
/// </summary>
public sealed class SalesforceRestClient
{
    public const string DefaultApiVersion = "67.0";

    private readonly OrgService _orgService;
    private readonly AppLog _log;
    private readonly HttpClient _http;

    public SalesforceRestClient(OrgService orgService, AppLog log)
    {
        _orgService = orgService;
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
    }

    /// <summary>SOQL を実行する（1 ページ目のみ）。ページングは nextRecordsUrl を GetPageAsync で辿る。</summary>
    public async Task<JsonDocument> QueryAsync(string targetOrg, string soql, bool useToolingApi = false, CancellationToken cancellationToken = default)
    {
        var apiVersion = await GetApiVersionAsync(targetOrg, cancellationToken).ConfigureAwait(false);
        var path = useToolingApi
            ? $"/services/data/v{apiVersion}/tooling/query?q={Uri.EscapeDataString(soql)}"
            : $"/services/data/v{apiVersion}/query?q={Uri.EscapeDataString(soql)}";

        var body = await SendForBodyAsync(targetOrg, HttpMethod.Get, path, null, cancellationToken, retryOn401: true).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    /// <summary>nextRecordsUrl（相対パス）で次ページを取得する。</summary>
    public async Task<JsonDocument> GetPageAsync(string targetOrg, string nextRecordsUrl, CancellationToken cancellationToken = default)
    {
        var body = await SendForBodyAsync(targetOrg, HttpMethod.Get, nextRecordsUrl, null, cancellationToken, retryOn401: true).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    /// <summary>組織のリミット情報を取得する。</summary>
    public async Task<JsonDocument> GetLimitsAsync(string targetOrg, CancellationToken cancellationToken = default)
    {
        var apiVersion = await GetApiVersionAsync(targetOrg, cancellationToken).ConfigureAwait(false);
        var body = await SendForBodyAsync(targetOrg, HttpMethod.Get, $"/services/data/v{apiVersion}/limits", null, cancellationToken, retryOn401: true).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    /// <summary>sObject の describe 情報を取得する。</summary>
    public async Task<JsonDocument> DescribeAsync(string targetOrg, string sobjectName, CancellationToken cancellationToken = default)
    {
        var apiVersion = await GetApiVersionAsync(targetOrg, cancellationToken).ConfigureAwait(false);
        var body = await SendForBodyAsync(
            targetOrg,
            HttpMethod.Get,
            $"/services/data/v{apiVersion}/sobjects/{Uri.EscapeDataString(sobjectName)}/describe",
            null,
            cancellationToken,
            retryOn401: true).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    /// <summary>任意のリクエストを実行してレスポンス本文を返す（失敗時は例外）。</summary>
    public async Task<string> SendRawAsync(string targetOrg, HttpMethod method, string pathOrUrl, string? jsonBody = null, CancellationToken cancellationToken = default)
    {
        var (statusCode, body) = await ExecuteAsync(targetOrg, method, pathOrUrl, jsonBody, cancellationToken, retryOn401: true).ConfigureAwait(false);
        EnsureSuccess(statusCode, body);
        return body;
    }

    /// <summary>ステータスコードも返す（REST コンソール用。エラーでも例外にしない）。</summary>
    public async Task<(HttpStatusCode StatusCode, string Body)> SendConsoleAsync(string targetOrg, HttpMethod method, string pathOrUrl, string? jsonBody = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(targetOrg, method, pathOrUrl, jsonBody, cancellationToken, retryOn401: true).ConfigureAwait(false);
    }

    private async Task<string> SendForBodyAsync(
        string targetOrg,
        HttpMethod method,
        string pathOrUrl,
        string? jsonBody,
        CancellationToken cancellationToken,
        bool retryOn401)
    {
        var (statusCode, body) = await ExecuteAsync(targetOrg, method, pathOrUrl, jsonBody, cancellationToken, retryOn401).ConfigureAwait(false);
        EnsureSuccess(statusCode, body);
        return body;
    }

    private async Task<(HttpStatusCode StatusCode, string Body)> ExecuteAsync(
        string targetOrg,
        HttpMethod method,
        string pathOrUrl,
        string? jsonBody,
        CancellationToken cancellationToken,
        bool retryOn401)
    {
        var auth = await _orgService.GetAuthAsync(targetOrg, cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = BuildUrl(auth.InstanceUrl, pathOrUrl);

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized && retryOn401)
        {
            _log.Warn($"{targetOrg} のトークンが無効のため再取得します（HTTP 401）。");
            _orgService.InvalidateAuth(targetOrg);
            return await ExecuteAsync(targetOrg, method, pathOrUrl, jsonBody, cancellationToken, retryOn401: false).ConfigureAwait(false);
        }

        return (response.StatusCode, body);
    }

    private static void EnsureSuccess(HttpStatusCode statusCode, string body)
    {
        if ((int)statusCode is < 200 or > 299)
        {
            throw CreateApiException(statusCode, body);
        }
    }

    private async Task<string> GetApiVersionAsync(string targetOrg, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await _orgService.GetAuthAsync(targetOrg, cancellationToken: cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(auth.ApiVersion) ? DefaultApiVersion : auth.ApiVersion;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warn($"API バージョンを取得できないため既定値 {DefaultApiVersion} を使用します: {ex.Message}");
            return DefaultApiVersion;
        }
    }

    private static string BuildUrl(string? instanceUrl, string pathOrUrl)
    {
        if (pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return pathOrUrl;
        }

        if (string.IsNullOrWhiteSpace(instanceUrl))
        {
            throw new SalesforceApiException("組織の instanceUrl を取得できませんでした。");
        }

        var baseUrl = instanceUrl.TrimEnd('/');
        return pathOrUrl.StartsWith('/') ? baseUrl + pathOrUrl : $"{baseUrl}/{pathOrUrl}";
    }

    private static SalesforceApiException CreateApiException(HttpStatusCode statusCode, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                var errorCode = root.TryGetProperty("errorCode", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                return new SalesforceApiException(message ?? $"HTTP {(int)statusCode} ({statusCode})", errorCode, statusCode);
            }
        }
        catch (JsonException)
        {
            // JSON でないエラーレスポンスはそのままメッセージに含める
        }

        var excerpt = body.Trim();
        if (excerpt.Length > 300)
        {
            excerpt = excerpt[..300] + "…";
        }

        return new SalesforceApiException($"HTTP {(int)statusCode} ({statusCode}): {excerpt}", null, statusCode);
    }
}
