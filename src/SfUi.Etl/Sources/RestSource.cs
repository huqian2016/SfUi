using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SfUi.Etl.Sources;

/// <summary>REST 入力ソースの設定。</summary>
public sealed class RestSourceOptions
{
    /// <summary>取得先 URL（http/https の絶対 URL）。</summary>
    public string Url { get; set; } = "";

    /// <summary>認証方式: None / Bearer / Basic / Header（カスタム ヘッダー）。</summary>
    public string AuthKind { get; set; } = "None";

    /// <summary>Bearer トークン（AuthKind = Bearer のとき使用）。</summary>
    public string? BearerToken { get; set; }

    /// <summary>Basic 認証ユーザー名（AuthKind = Basic のとき使用）。</summary>
    public string? BasicUser { get; set; }

    /// <summary>Basic 認証パスワード（AuthKind = Basic のとき使用）。</summary>
    public string? BasicPassword { get; set; }

    /// <summary>カスタム ヘッダー（AuthKind = Header のとき使用）。"名前: 値" を改行区切り（# 始まりは無視）。</summary>
    public string? Headers { get; set; }

    /// <summary>ページング方式: None（1 回） / Offset（offset + limit） / Link（Link ヘッダー rel="next"） / Cursor（応答内の次カーソル）。</summary>
    public string Paging { get; set; } = "None";

    /// <summary>Offset ページングの offset パラメーター名。</summary>
    public string OffsetParameter { get; set; } = "offset";

    /// <summary>Offset ページングの limit パラメーター名。</summary>
    public string LimitParameter { get; set; } = "limit";

    /// <summary>Cursor ページングのカーソル パラメーター名。</summary>
    public string CursorParameter { get; set; } = "cursor";

    /// <summary>Cursor ページングで次カーソルを読む応答プロパティ名。</summary>
    public string CursorProperty { get; set; } = "nextCursor";

    /// <summary>応答ルートがオブジェクトの場合にレコード配列を持つプロパティ名。</summary>
    public string? ArrayProperty { get; set; }

    /// <summary>Offset ページングの 1 ページあたり件数。</summary>
    public int PageSize { get; set; } = 200;

    /// <summary>最大ページ数（無限ループ防止）。</summary>
    public int MaxPages { get; set; } = 100;

    /// <summary>HTTP タイムアウト秒（既定クライアント使用時）。</summary>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// 汎用 REST API ソース。GET で JSON を取得し、各レコードをフラット化して読み込む
/// （値は文字列 / 数値原文 / true / false / null、ネストした値は生 JSON テキスト）。
/// ページング: None / Offset / Link / Cursor。認証: None / Bearer / Basic / カスタム ヘッダー。
/// </summary>
public sealed class RestSource : IEtlSource
{
    private readonly List<Dictionary<string, object?>> _records = new();
    private readonly List<string> _columns = new();

    /// <summary>設定に従って全ページを取得する（コンストラクターで同期ロード）。</summary>
    public RestSource(RestSourceOptions options)
        : this(options, null)
    {
    }

    /// <summary>テスト用: 任意の <see cref="HttpClient"/>（独自ハンドラー）で実行する。</summary>
    internal RestSource(RestSourceOptions options, HttpClient? client)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Url);
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("REST URL には http/https の絶対 URL を指定してください。", nameof(options));
        }

        Options = options;
        Url = options.Url;
        Name = DescribeName(uri);

        var http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)) };
        try
        {
            // 同期コンストラクターから安全に待つ（UI スレッドから呼ばれてもデッドロックしない）
            Task.Run(() => LoadAllAsync(http, options)).GetAwaiter().GetResult();
        }
        finally
        {
            if (client is null)
            {
                http.Dispose();
            }
        }
    }

    /// <summary>設定。</summary>
    public RestSourceOptions Options { get; }

    /// <summary>取得先 URL。</summary>
    public string Url { get; }

    public string Name { get; }

    public IReadOnlyList<string> Columns => _columns;

    public IEnumerable<object?[]> ReadRows()
    {
        foreach (var record in _records)
        {
            var row = new object?[_columns.Count];
            for (var i = 0; i < _columns.Count; i++)
            {
                record.TryGetValue(_columns[i], out row[i]);
            }

            yield return row;
        }
    }

    private async Task LoadAllAsync(HttpClient http, RestSourceOptions options)
    {
        var paging = (options.Paging ?? "None").Trim();
        if (!paging.Equals("None", StringComparison.OrdinalIgnoreCase) &&
            !paging.Equals("Offset", StringComparison.OrdinalIgnoreCase) &&
            !paging.Equals("Link", StringComparison.OrdinalIgnoreCase) &&
            !paging.Equals("Cursor", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"不明なページング方式です: {options.Paging}", nameof(options));
        }

        var url = options.Url;
        var offset = 0;
        var cursor = (string?)null;
        var pageCount = 0;
        var maxPages = Math.Max(1, options.MaxPages);

        while (true)
        {
            pageCount++;
            if (pageCount > maxPages)
            {
                throw new InvalidOperationException($"REST 取得が最大ページ数 ({options.MaxPages}) を超えました。");
            }

            var pageUrl = BuildPageUrl(options, paging, url, offset, cursor);
            var page = await HttpGetAsync(http, options, pageUrl).ConfigureAwait(false);
            if (page.StatusCode < 200 || page.StatusCode > 299)
            {
                throw new InvalidOperationException($"REST 取得に失敗しました (HTTP {page.StatusCode}): {Truncate(page.Body, 200)}");
            }

            var (added, nextCursor) = AddPage(options, page.Body);

            if (paging.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (paging.Equals("Offset", StringComparison.OrdinalIgnoreCase))
            {
                offset += added;
                if (added < Math.Max(1, options.PageSize))
                {
                    break;
                }

                continue;
            }

            if (paging.Equals("Link", StringComparison.OrdinalIgnoreCase))
            {
                var next = ExtractNextLink(page.LinkHeader);
                if (string.IsNullOrEmpty(next))
                {
                    break;
                }

                url = next;
                continue;
            }

            // Cursor
            if (string.IsNullOrEmpty(nextCursor))
            {
                break;
            }

            cursor = nextCursor;
        }
    }

    private static async Task<RestFetchResult> HttpGetAsync(HttpClient http, RestSourceOptions options, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(request, options);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var link = response.Headers.TryGetValues("Link", out var values) ? string.Join(",", values) : null;
        return new RestFetchResult((int)response.StatusCode, body, link);
    }

    private (int Added, string? NextCursor) AddPage(RestSourceOptions options, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (0, null);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                body,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"REST 応答を JSON として解釈できません: {Truncate(body, 200)}", ex);
        }

        using (document)
        {
            var items = JsonRecords.ResolveItems(document.RootElement, options.ArrayProperty);
            foreach (var item in items)
            {
                JsonRecords.Add(_columns, _records, item);
            }

            string? nextCursor = null;
            if (string.Equals(options.Paging?.Trim(), "Cursor", StringComparison.OrdinalIgnoreCase) &&
                document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(options.CursorProperty, out var node) &&
                node.ValueKind == JsonValueKind.String)
            {
                nextCursor = node.GetString();
            }

            return (items.Count, nextCursor);
        }
    }

    private static string BuildPageUrl(RestSourceOptions options, string paging, string baseUrl, int offset, string? cursor)
    {
        if (paging.Equals("Offset", StringComparison.OrdinalIgnoreCase))
        {
            var withOffset = AppendQuery(baseUrl, options.OffsetParameter, offset.ToString(CultureInfo.InvariantCulture));
            return AppendQuery(withOffset, options.LimitParameter, Math.Max(1, options.PageSize).ToString(CultureInfo.InvariantCulture));
        }

        if (paging.Equals("Cursor", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(cursor))
        {
            return AppendQuery(baseUrl, options.CursorParameter, cursor);
        }

        return baseUrl;
    }

    private static string AppendQuery(string url, string name, string value)
    {
        var separator = url.Contains('?')
            ? url.EndsWith('?') || url.EndsWith('&') ? "" : "&"
            : "?";
        return url + separator + Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value);
    }

    private static void ApplyAuth(HttpRequestMessage request, RestSourceOptions options)
    {
        switch ((options.AuthKind ?? "None").Trim().ToLowerInvariant())
        {
            case "" or "none":
                break;

            case "bearer":
                if (!string.IsNullOrEmpty(options.BearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.BearerToken);
                }

                break;

            case "basic":
                var raw = (options.BasicUser ?? "") + ":" + (options.BasicPassword ?? "");
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
                break;

            case "header":
                foreach (var (name, value) in ParseHeaderLines(options.Headers))
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }

                break;

            default:
                throw new ArgumentException($"不明な認証方式です: {options.AuthKind}", nameof(options));
        }
    }

    /// <summary>"名前: 値" 形式の行（改行区切り）を解析する。空行と # 始まりの行は無視。</summary>
    internal static List<(string Name, string Value)> ParseHeaderLines(string? text)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ', '\t');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var index = line.IndexOf(':');
            if (index <= 0)
            {
                continue;
            }

            var name = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (name.Length > 0)
            {
                result.Add((name, value));
            }
        }

        return result;
    }

    private static readonly Regex NextLinkPattern = new(
        "<([^>]+)>\\s*;\\s*rel\\s*=\\s*\"?next\"?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string? ExtractNextLink(string? linkHeader)
    {
        if (string.IsNullOrWhiteSpace(linkHeader))
        {
            return null;
        }

        foreach (var part in linkHeader.Split(','))
        {
            var match = NextLinkPattern.Match(part);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static string DescribeName(Uri uri)
    {
        var path = uri.AbsolutePath.Trim('/');
        if (path.Length == 0)
        {
            return uri.Host;
        }

        var segment = path[(path.LastIndexOf('/') + 1)..];
        return segment.Length == 0 ? uri.Host : segment;
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";

    private readonly record struct RestFetchResult(int StatusCode, string Body, string? LinkHeader);
}
