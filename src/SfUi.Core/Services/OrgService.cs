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
            throw new SfCliException($"組織一覧の取得に失敗しました: {command.ErrorMessage}", raw);
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
    /// インストール済みの sf v2.94.6 には org auth show-access-token が無いため org display を使用する。
    /// </summary>
    public async Task<OrgAuthInfo> GetAuthAsync(string targetOrg, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetOrg))
        {
            throw new ArgumentException("対象組織が指定されていません。", nameof(targetOrg));
        }

        if (!forceRefresh && _authCache.TryGetValue(targetOrg, out var cached) && cached.IsFresh(TokenCacheDuration))
        {
            return cached;
        }

        var raw = await _runner
            .RunAsync(new[] { "org", "display", "--target-org", targetOrg, "--json" }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var command = SfCommandResult.From(raw);
        var accessToken = command.GetResultString("accessToken");
        if (!command.IsSuccess || string.IsNullOrWhiteSpace(accessToken))
        {
            throw new SfCliException($"アクセストークンの取得に失敗しました ({targetOrg}): {command.ErrorMessage}", raw);
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

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
}
