using System.Collections.Concurrent;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>組織一覧・認証情報（アクセストークン）を sf CLI 経由で取得する。</summary>
public sealed class OrgService
{
    /// <summary>トークンのメモリキャッシュ保持期間。</summary>
    public static readonly TimeSpan TokenCacheDuration = TimeSpan.FromMinutes(30);

    private readonly SfCliRunner _runner;
    private readonly ConcurrentDictionary<string, OrgAuthInfo> _authCache = new(StringComparer.OrdinalIgnoreCase);

    public OrgService(SfCliRunner runner)
    {
        _runner = runner;
    }

    /// <summary>認証済み組織の一覧を取得する。</summary>
    public async Task<IReadOnlyList<OrgInfo>> ListOrgsAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _runner
            .RunAsync(new[] { "org", "list", "--json" }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var command = SfCommandResult.From(raw);
        if (!command.IsSuccess)
        {
            throw new SfCliException(UiText.T("Core_OrgListFailedFmt", command.ErrorMessage), raw);
        }

        return ParseOrgList(command.Result);
    }

    /// <summary>
    /// result 要素（カテゴリごとの配列を含む）から組織一覧を組み立てる。
    /// 同じ組織が複数カテゴリ（other / sandboxes / nonScratchOrgs 等）に重複して現れるため、username で重複排除する。
    /// </summary>
    public static IReadOnlyList<OrgInfo> ParseOrgList(JsonElement? result)
    {
        var orgs = new List<OrgInfo>();
        if (result is not { } root || root.ValueKind != JsonValueKind.Object)
        {
            return orgs;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in root.EnumerateObject())
        {
            if (category.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in category.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var username = GetString(item, "username");
                if (string.IsNullOrWhiteSpace(username) || !seen.Add(username))
                {
                    continue;
                }

                var isDefault = GetBool(item, "isDefaultUsername")
                                || string.Equals(GetString(item, "defaultMarker"), "(D)", StringComparison.Ordinal);

                orgs.Add(new OrgInfo(
                    Username: username,
                    Alias: GetString(item, "alias"),
                    OrgId: GetString(item, "orgId"),
                    InstanceUrl: GetString(item, "instanceUrl"),
                    ConnectedStatus: GetString(item, "connectedStatus"),
                    IsDefault: isDefault,
                    IsSandbox: GetBool(item, "isSandbox")));
            }
        }

        return orgs;
    }

    /// <summary>
    /// 対象組織のアクセストークン等を取得する（既定 30 分キャッシュ、forceRefresh で再取得）。
    /// アクセストークンは sf 2.152+ で <c>org display</c> の出力から隠されるため、
    /// <c>org auth show-access-token --json</c> で取得し、instanceUrl / apiVersion は <c>org display</c> から得る。
    /// </summary>
    public async Task<OrgAuthInfo> GetAuthAsync(string targetOrg, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetOrg))
        {
            throw new ArgumentException(UiText.T("Core_OrgSelectRequired"), nameof(targetOrg));
        }

        if (!forceRefresh && _authCache.TryGetValue(targetOrg, out var cached) && cached.IsFresh(TokenCacheDuration))
        {
            return cached;
        }

        var raw = await _runner
            .RunAsync(new[] { "org", "display", "--target-org", targetOrg, "--json" }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var command = SfCommandResult.From(raw);

        // sf org display はシークレットを隠す（accessToken が "[REDACTED]..." になる）ため、
        // トークンは専用コマンド org auth show-access-token --json で取得する（--json で確認プロンプトをスキップ）。
        var tokenRaw = await _runner
            .RunAsync(new[] { "org", "auth", "show-access-token", "--target-org", targetOrg, "--json" }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var tokenCommand = SfCommandResult.From(tokenRaw);
        var accessToken = tokenCommand.GetResultString("accessToken");

        if (!command.IsSuccess || !tokenCommand.IsSuccess || string.IsNullOrWhiteSpace(accessToken))
        {
            var error = !command.IsSuccess ? command.ErrorMessage : tokenCommand.ErrorMessage;
            throw new SfCliException(UiText.T("Core_TokenFailedFmt", targetOrg, error), raw);
        }

        var auth = new OrgAuthInfo(
            TargetOrg: targetOrg,
            Username: command.GetResultString("username"),
            InstanceUrl: command.GetResultString("instanceUrl"),
            AccessToken: accessToken,
            ApiVersion: command.GetResultString("apiVersion") ?? command.GetResultString("instanceApiVersion"),
            FetchedAt: DateTimeOffset.Now);

        _authCache[targetOrg] = auth;
        return auth;
    }

    /// <summary>トークンキャッシュを破棄する（401 時の再取得などに使用）。</summary>
    public void InvalidateAuth(string targetOrg) => _authCache.TryRemove(targetOrg, out _);

    /// <summary>
    /// セッション付き（frontdoor.jsp?sid=...）の組織 URL を <c>sf org open --url-only</c> で取得する。
    /// この URL をブラウザーで開くと再ログイン不要で直接アクセスできる。
    /// 取得に失敗した場合は null を返す（呼び出し側で通常 URL にフォールバックする）。
    /// </summary>
    public async Task<string?> GetFrontDoorUrlAsync(
        string targetOrg,
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetOrg))
        {
            return null;
        }

        var args = new List<string> { "org", "open", "--url-only", "--target-org", targetOrg };
        if (!string.IsNullOrWhiteSpace(path))
        {
            args.Add("--path");
            args.Add(path);
        }

        args.Add("--json");

        try
        {
            var raw = await _runner.RunAsync(args, cancellationToken: cancellationToken).ConfigureAwait(false);
            var command = SfCommandResult.From(raw);
            return command.IsSuccess ? ParseFrontDoorUrl(command.Result) : null;
        }
        catch (Exception)
        {
            // 認証なし・タイムアウト等はフォールバック対象（例外にしない）
            return null;
        }
    }

    /// <summary>sf org open --url-only の応答（result.url）から frontdoor URL を取り出す。</summary>
    public static string? ParseFrontDoorUrl(JsonElement? result)
    {
        if (result is not { } root || root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var url = urlElement.GetString();
        return !string.IsNullOrWhiteSpace(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url
            : null;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
}
