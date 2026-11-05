namespace SfUi.Core;

/// <summary>
/// 内部ブラウザーでの組織登録に使う URL の正規化・判定（純関数）。
/// </summary>
public static class OrgSignInUrl
{
    /// <summary>本番組織のログイン URL。</summary>
    public const string LoginUrl = "https://login.salesforce.com";

    /// <summary>Sandbox / 開発組織のログイン URL。</summary>
    public const string SandboxLoginUrl = "https://test.salesforce.com";

    private static readonly string[] LoginHosts =
    {
        "login.salesforce.com",
        "test.salesforce.com",
        "login.salesforce.mil",
    };

    /// <summary>入力された URL をスキーム付きのオリジンへ正規化する（空なら本番ログイン URL）。</summary>
    public static string Normalize(string? input)
    {
        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return LoginUrl;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? uri.GetLeftPart(UriPartial.Authority)
            : LoginUrl;
    }

    /// <summary>Salesforce のログイン ドメインか（組織のインスタンスではない）。</summary>
    public static bool IsLoginHost(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return LoginHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>URL が組織のインスタンス（my.salesforce.com / force.com など）ならそのオリジンを返す。</summary>
    public static bool TryGetInstanceUrl(string? url, out string instanceUrl)
    {
        instanceUrl = string.Empty;
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var host = uri.Host;
        if (host.Length == 0 || IsLoginHost(url))
        {
            return false;
        }

        var isSalesforceHost = host.EndsWith(".salesforce.com", StringComparison.OrdinalIgnoreCase)
                               || host.EndsWith(".force.com", StringComparison.OrdinalIgnoreCase)
                               || host.EndsWith(".cloudforce.com", StringComparison.OrdinalIgnoreCase);
        if (!isSalesforceHost)
        {
            return false;
        }

        instanceUrl = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    /// <summary>セッション Cookie（sid）の値を取り出す（空文字は無視）。</summary>
    public static string? FindSessionCookie(IEnumerable<(string Name, string Value)>? cookies)
    {
        if (cookies is null)
        {
            return null;
        }

        foreach (var cookie in cookies)
        {
            if (string.Equals(cookie.Name, "sid", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(cookie.Value))
            {
                return cookie.Value;
            }
        }

        return null;
    }
}
