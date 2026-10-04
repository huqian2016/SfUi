using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// Permission Sets / Permission Set Groups / Profiles の権限（ObjectPermissions / FieldPermissions）を REST で取得する。
/// PSG の権限は構成する権限セット（PermissionSetGroupComponent）の和集合で近似する（ミューティングは考慮しない）。
/// </summary>
public sealed class PermissionAccessService
{
    /// <summary>ObjectPermissions / FieldPermissions の取得上限（十分大きいが無限ループ防止）。</summary>
    private const int MaxRows = 100_000;

    private readonly SalesforceRestClient _rest;
    private readonly AppLog _log;

    /// <summary>組織ごとの主体カタログ キャッシュ。</summary>
    private readonly Dictionary<string, PermissionCatalog> _catalogCache = new(StringComparer.OrdinalIgnoreCase);

    public PermissionAccessService(SalesforceRestClient rest, AppLog log)
    {
        _rest = rest;
        _log = log;
    }

    /// <summary>キャッシュを破棄する（再取得ボタン用）。</summary>
    public void Invalidate(string targetOrg)
    {
        lock (_catalogCache)
        {
            _catalogCache.Remove(targetOrg);
        }
    }

    /// <summary>主体カタログ（プロファイル / 権限セット / 権限セットグループ + PSG 構成）を取得する。</summary>
    public async Task<PermissionCatalog> GetCatalogAsync(string targetOrg, CancellationToken cancellationToken = default)
    {
        lock (_catalogCache)
        {
            if (_catalogCache.TryGetValue(targetOrg, out var cached))
            {
                return cached;
            }
        }

        var permissionSets = await RestQueryPager.QueryAllAsync(
            _rest, targetOrg,
            "SELECT Id, Name, Label, IsCustom, IsOwnedByProfile, Profile.Name FROM PermissionSet",
            MaxRows, cancellationToken).ConfigureAwait(false);
        var groups = await RestQueryPager.QueryAllAsync(
            _rest, targetOrg,
            "SELECT Id, MasterLabel, DeveloperName FROM PermissionSetGroup",
            MaxRows, cancellationToken).ConfigureAwait(false);
        var components = await RestQueryPager.QueryAllAsync(
            _rest, targetOrg,
            "SELECT PermissionSetGroupId, PermissionSetId FROM PermissionSetGroupComponent",
            MaxRows, cancellationToken).ConfigureAwait(false);

        var catalog = BuildCatalog(permissionSets.Records, groups.Records, components.Records);
        _log.Info($"権限カタログを取得: {targetOrg} (主体 {catalog.Subjects.Count} 件 / PSG 構成 {catalog.GroupComponents.Count} グループ)");
        lock (_catalogCache)
        {
            _catalogCache[targetOrg] = catalog;
        }

        return catalog;
    }

    /// <summary>選択中オブジェクトの権限を主体ごとに取得する（全主体を 1 行ずつ返す）。</summary>
    public async Task<IReadOnlyList<ObjectAccessRow>> GetObjectAccessAsync(
        string targetOrg,
        string objectApiName,
        CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(targetOrg, cancellationToken).ConfigureAwait(false);
        var rows = await RestQueryPager.QueryAllAsync(
            _rest, targetOrg,
            "SELECT ParentId, PermissionsRead, PermissionsCreate, PermissionsEdit, PermissionsDelete, " +
            "PermissionsViewAllRecords, PermissionsModifyAllRecords, PermissionsViewAllFields " +
            $"FROM ObjectPermissions WHERE SobjectType = '{OrgInfoQueryBuilder.EscapeSoqlString(objectApiName)}'",
            MaxRows, cancellationToken).ConfigureAwait(false);
        return BuildObjectAccess(catalog, rows.Records);
    }

    /// <summary>選択中オブジェクトの項目権限を主体ごとに取得する（FieldPermissions の明示行のみ）。</summary>
    public async Task<FieldAccessSnapshot> GetFieldAccessAsync(
        string targetOrg,
        string objectApiName,
        CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(targetOrg, cancellationToken).ConfigureAwait(false);
        var rows = await RestQueryPager.QueryAllAsync(
            _rest, targetOrg,
            "SELECT ParentId, Field, PermissionsRead, PermissionsEdit " +
            $"FROM FieldPermissions WHERE SobjectType = '{OrgInfoQueryBuilder.EscapeSoqlString(objectApiName)}'",
            MaxRows, cancellationToken).ConfigureAwait(false);
        return BuildFieldAccess(catalog, rows.Records);
    }

    // ---------- 解析（テスト用に公開） ----------

    /// <summary>PermissionSet / PermissionSetGroup / PermissionSetGroupComponent の行からカタログを構築する。</summary>
    public static PermissionCatalog BuildCatalog(
        IReadOnlyList<JsonElement> permissionSets,
        IReadOnlyList<JsonElement> groups,
        IReadOnlyList<JsonElement> components)
    {
        var subjects = new List<PermissionSubject>();
        foreach (var row in permissionSets)
        {
            var id = GetString(row, "Id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            if (GetBool(row, "IsOwnedByProfile"))
            {
                // プロファイル所有の権限セット = プロファイル本体。ラベルには Profile.Name を使う。
                var name = GetNestedString(row, "Profile", "Name");
                if (string.IsNullOrEmpty(name))
                {
                    name = GetString(row, "Label");
                }

                subjects.Add(new PermissionSubject(id, PermissionSubjectKind.Profile, name, name, false));
            }
            else
            {
                subjects.Add(new PermissionSubject(
                    id,
                    PermissionSubjectKind.PermissionSet,
                    GetString(row, "Label"),
                    GetString(row, "Name"),
                    GetBool(row, "IsCustom")));
            }
        }

        foreach (var row in groups)
        {
            var id = GetString(row, "Id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            // PermissionSetGroup には IsCustom フィールドが無く常にユーザー作成のため custom = true とする。
            subjects.Add(new PermissionSubject(
                id,
                PermissionSubjectKind.PermissionSetGroup,
                GetString(row, "MasterLabel"),
                GetString(row, "DeveloperName"),
                true));
        }

        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var row in components)
        {
            var groupId = GetString(row, "PermissionSetGroupId");
            var permissionSetId = GetString(row, "PermissionSetId");
            if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(permissionSetId))
            {
                continue;
            }

            if (!map.TryGetValue(groupId, out var list))
            {
                list = new List<string>();
                map[groupId] = list;
            }

            list.Add(permissionSetId);
        }

        var sorted = subjects
            .OrderBy(s => s.Kind)
            .ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.ApiName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var componentsReadOnly = map.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
        return new PermissionCatalog(sorted, componentsReadOnly);
    }

    /// <summary>ObjectPermissions の行を主体ごとの行（PSG は構成 PS の和集合）に変換する。</summary>
    public static IReadOnlyList<ObjectAccessRow> BuildObjectAccess(
        PermissionCatalog catalog,
        IReadOnlyList<JsonElement> permissionRows)
    {
        var byParent = new Dictionary<string, ObjectFlagSet>(StringComparer.Ordinal);
        foreach (var row in permissionRows)
        {
            var parentId = GetString(row, "ParentId");
            if (string.IsNullOrEmpty(parentId))
            {
                continue;
            }

            var flags = new ObjectFlagSet(
                GetBool(row, "PermissionsRead"),
                GetBool(row, "PermissionsCreate"),
                GetBool(row, "PermissionsEdit"),
                GetBool(row, "PermissionsDelete"),
                GetBool(row, "PermissionsViewAllRecords"),
                GetBool(row, "PermissionsModifyAllRecords"),
                GetBool(row, "PermissionsViewAllFields"));
            byParent[parentId] = byParent.TryGetValue(parentId, out var existing) ? existing.Merge(flags) : flags;
        }

        var rows = new List<ObjectAccessRow>(catalog.Subjects.Count);
        foreach (var subject in catalog.Subjects)
        {
            var flags = subject.Kind == PermissionSubjectKind.PermissionSetGroup
                ? MergeGroupFlags(catalog, subject.Id, byParent)
                : byParent.TryGetValue(subject.Id, out var own) ? own : ObjectFlagSet.None;
            rows.Add(flags.ToRow(subject));
        }

        return rows;
    }

    /// <summary>FieldPermissions の行を主体ごとの項目セル（PSG は構成 PS の和集合）に変換する。</summary>
    public static FieldAccessSnapshot BuildFieldAccess(
        PermissionCatalog catalog,
        IReadOnlyList<JsonElement> permissionRows)
    {
        var byParent = new Dictionary<string, Dictionary<string, FieldAccessCell>>(StringComparer.Ordinal);
        foreach (var row in permissionRows)
        {
            var parentId = GetString(row, "ParentId");
            var field = GetString(row, "Field");
            if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(field))
            {
                continue;
            }

            if (!byParent.TryGetValue(parentId, out var map))
            {
                map = new Dictionary<string, FieldAccessCell>(StringComparer.OrdinalIgnoreCase);
                byParent[parentId] = map;
            }

            var cell = new FieldAccessCell(GetBool(row, "PermissionsRead"), GetBool(row, "PermissionsEdit"));
            map[field] = map.TryGetValue(field, out var existing) ? existing.Merge(cell) : cell;
        }

        var result = new Dictionary<string, IReadOnlyDictionary<string, FieldAccessCell>>(StringComparer.Ordinal);
        foreach (var subject in catalog.Subjects)
        {
            if (subject.Kind == PermissionSubjectKind.PermissionSetGroup)
            {
                var merged = new Dictionary<string, FieldAccessCell>(StringComparer.OrdinalIgnoreCase);
                if (catalog.GroupComponents.TryGetValue(subject.Id, out var members))
                {
                    foreach (var memberId in members)
                    {
                        if (!byParent.TryGetValue(memberId, out var memberMap))
                        {
                            continue;
                        }

                        foreach (var pair in memberMap)
                        {
                            merged[pair.Key] = merged.TryGetValue(pair.Key, out var existing)
                                ? existing.Merge(pair.Value)
                                : pair.Value;
                        }
                    }
                }

                result[subject.Id] = merged;
            }
            else
            {
                result[subject.Id] = byParent.TryGetValue(subject.Id, out var own)
                    ? own
                    : new Dictionary<string, FieldAccessCell>(StringComparer.OrdinalIgnoreCase);
            }
        }

        return new FieldAccessSnapshot(result);
    }

    /// <summary>FieldPermissions.Field（例: Account.Name）から項目 API 名を取り出す。</summary>
    public static string FieldShortName(string fieldName)
    {
        var index = fieldName.LastIndexOf('.');
        return index >= 0 && index < fieldName.Length - 1 ? fieldName[(index + 1)..] : fieldName;
    }

    // ---------- 内部 ----------

    private static ObjectFlagSet MergeGroupFlags(
        PermissionCatalog catalog,
        string groupId,
        Dictionary<string, ObjectFlagSet> byParent)
    {
        var flags = ObjectFlagSet.None;
        if (catalog.GroupComponents.TryGetValue(groupId, out var members))
        {
            foreach (var memberId in members)
            {
                if (byParent.TryGetValue(memberId, out var memberFlags))
                {
                    flags = flags.Merge(memberFlags);
                }
            }
        }

        return flags;
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string GetNestedString(JsonElement element, string parentName, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(parentName, out var parent) &&
        parent.ValueKind == JsonValueKind.Object
            ? GetString(parent, propertyName)
            : string.Empty;

    private static bool GetBool(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private readonly record struct ObjectFlagSet(
        bool Read,
        bool Create,
        bool Edit,
        bool Delete,
        bool ViewAllRecords,
        bool ModifyAllRecords,
        bool ViewAllFields)
    {
        public static ObjectFlagSet None { get; } = new(false, false, false, false, false, false, false);

        public ObjectFlagSet Merge(ObjectFlagSet other) => new(
            Read || other.Read,
            Create || other.Create,
            Edit || other.Edit,
            Delete || other.Delete,
            ViewAllRecords || other.ViewAllRecords,
            ModifyAllRecords || other.ModifyAllRecords,
            ViewAllFields || other.ViewAllFields);

        public ObjectAccessRow ToRow(PermissionSubject subject) => new(
            subject, Read, Create, Edit, Delete, ViewAllRecords, ModifyAllRecords, ViewAllFields);
    }
}

/// <summary>項目アクセス セルの OR 合成。</summary>
internal static class FieldAccessCellExtensions
{
    public static FieldAccessCell Merge(this FieldAccessCell left, FieldAccessCell right) =>
        new(left.Read || right.Read, left.Edit || right.Edit);
}
