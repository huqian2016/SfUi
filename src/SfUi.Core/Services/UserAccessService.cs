using System.Collections.Concurrent;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// ユーザー中心アクセスのために、有効ユーザー（プロファイル付き）と権限セットの割り当てを REST で取得する。
/// </summary>
public sealed class UserAccessService
{
    /// <summary>割り当て取得時の AssigneeId IN 句の 1 回あたり件数。</summary>
    public const int AssignmentChunkSize = 200;

    private readonly SalesforceRestClient _rest;
    private readonly AppLog _log;
    private readonly ConcurrentDictionary<string, IReadOnlyList<AccessUser>> _users = new(StringComparer.OrdinalIgnoreCase);

    public UserAccessService(SalesforceRestClient rest, AppLog log)
    {
        _rest = rest;
        _log = log;
    }

    /// <summary>有効ユーザー一覧（Id / 名前 / ユーザー名 / プロファイル）を返す（キャッシュ優先）。</summary>
    public async Task<IReadOnlyList<AccessUser>> ListUsersAsync(
        string targetOrg,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _users.TryGetValue(targetOrg, out var cached))
        {
            return cached;
        }

        var result = await RestQueryPager.QueryAllAsync(
            _rest,
            targetOrg,
            "SELECT Id, Name, Username, ProfileId, Profile.Name FROM User WHERE IsActive = true ORDER BY Name",
            20_000,
            cancellationToken).ConfigureAwait(false);

        var list = ParseUsers(result.Records);
        _users[targetOrg] = list;
        _log.Info($"有効ユーザー一覧を取得: {targetOrg} ({list.Count} 件)");
        return list;
    }

    /// <summary>指定ユーザーの権限セット割り当て（PSG 割り当てを含む）を返す。200 件ずつ分割して問い合わせる。</summary>
    public async Task<IReadOnlyList<UserPermissionAssignment>> ListAssignmentsAsync(
        string targetOrg,
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var list = new List<UserPermissionAssignment>();
        if (userIds.Count == 0)
        {
            return list;
        }

        foreach (var chunk in Chunk(userIds, AssignmentChunkSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inList = string.Join(",", chunk.Select(id => $"'{OrgInfoQueryBuilder.EscapeSoqlString(id)}'"));
            var result = await RestQueryPager.QueryAllAsync(
                _rest,
                targetOrg,
                "SELECT AssigneeId, PermissionSetId, PermissionSetGroupId FROM PermissionSetAssignment " +
                $"WHERE AssigneeId IN ({inList})",
                100_000,
                cancellationToken).ConfigureAwait(false);
            list.AddRange(ParseAssignments(result.Records));
        }

        _log.Info($"権限セット割り当てを取得: {targetOrg} / ユーザー {userIds.Count} 人 ({list.Count} 行)");
        return list;
    }

    /// <summary>キャッシュを破棄する（組織の再読込用）。</summary>
    public void Invalidate(string targetOrg) => _users.TryRemove(targetOrg, out _);

    // ---------- 解析（テスト用に公開） ----------

    /// <summary>User の行を有効ユーザー一覧に変換する。</summary>
    public static IReadOnlyList<AccessUser> ParseUsers(IReadOnlyList<JsonElement> rows)
    {
        var list = new List<AccessUser>(rows.Count);
        foreach (var row in rows)
        {
            var id = GetString(row, "Id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var name = GetString(row, "Name") ?? id;
            var username = GetString(row, "Username") ?? string.Empty;
            var profileId = GetString(row, "ProfileId") ?? string.Empty;
            var profileName = GetNestedString(row, "Profile", "Name") ?? string.Empty;
            list.Add(new AccessUser(id, name, username, profileId, profileName));
        }

        return list;
    }

    /// <summary>PermissionSetAssignment の行を割り当て一覧に変換する。</summary>
    public static IReadOnlyList<UserPermissionAssignment> ParseAssignments(IReadOnlyList<JsonElement> rows)
    {
        var list = new List<UserPermissionAssignment>(rows.Count);
        foreach (var row in rows)
        {
            var userId = GetString(row, "AssigneeId");
            var permissionSetId = GetString(row, "PermissionSetId");
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(permissionSetId))
            {
                continue;
            }

            list.Add(new UserPermissionAssignment(userId, permissionSetId, GetString(row, "PermissionSetGroupId")));
        }

        return list;
    }

    private static IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyList<string> values, int size)
    {
        for (var i = 0; i < values.Count; i += size)
        {
            yield return values.Skip(i).Take(size).ToList();
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetNestedString(JsonElement element, string parent, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(parent, out var child) &&
        child.ValueKind == JsonValueKind.Object
            ? GetString(child, name)
            : null;
}
