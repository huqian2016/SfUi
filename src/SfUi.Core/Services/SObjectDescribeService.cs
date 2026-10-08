using System.Collections.Concurrent;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// オブジェクト一覧（DescribeGlobal）と項目メタデータ（describe）を取得し、
/// 組織単位でメモリ キャッシュする。
/// </summary>
public sealed class SObjectDescribeService
{
    private readonly SalesforceRestClient _rest;
    private readonly AppLog _log;
    private readonly ConcurrentDictionary<string, IReadOnlyList<DataIoObject>> _objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DataIoObjectDescribe> _describes = new(StringComparer.OrdinalIgnoreCase);

    public SObjectDescribeService(SalesforceRestClient rest, AppLog log)
    {
        _rest = rest;
        _log = log;
    }

    /// <summary>組織の sObject 一覧を返す（キャッシュ優先）。</summary>
    public async Task<IReadOnlyList<DataIoObject>> ListObjectsAsync(string targetOrg, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _objects.TryGetValue(targetOrg, out var cached))
        {
            return cached;
        }

        using var document = await _rest.DescribeGlobalAsync(targetOrg, cancellationToken).ConfigureAwait(false);
        var list = ParseObjects(document.RootElement);
        _objects[targetOrg] = list;
        _log.Info($"オブジェクト一覧を取得: {targetOrg} ({list.Count} 件)");
        return list;
    }

    /// <summary>オブジェクトの項目メタデータを返す（キャッシュ優先）。</summary>
    public async Task<DataIoObjectDescribe> DescribeAsync(string targetOrg, string objectName, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var key = targetOrg + "\n" + objectName;
        if (!forceRefresh && _describes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        using var document = await _rest.DescribeAsync(targetOrg, objectName, cancellationToken).ConfigureAwait(false);
        var describe = ParseDescribe(objectName, document.RootElement);
        _describes[key] = describe;
        return describe;
    }

    /// <summary>キャッシュを破棄する（オブジェクト再読込用）。</summary>
    public void Invalidate(string targetOrg)
    {
        _objects.TryRemove(targetOrg, out _);
        foreach (var key in _describes.Keys.Where(k => k.StartsWith(targetOrg + "\n", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _describes.TryRemove(key, out _);
        }
    }

    /// <summary>DescribeGlobal の応答をパースする（純関数・テスト対象）。</summary>
    public static IReadOnlyList<DataIoObject> ParseObjects(JsonElement root)
    {
        if (!root.TryGetProperty("sobjects", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<DataIoObject>();
        }

        var list = new List<DataIoObject>();
        foreach (var item in array.EnumerateArray())
        {
            var name = GetString(item, "name");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            list.Add(new DataIoObject(
                name,
                GetString(item, "label") ?? name,
                GetBool(item, "queryable"),
                GetBool(item, "createable"),
                GetBool(item, "updateable"),
                GetBool(item, "deletable")));
        }

        return list.OrderBy(o => o.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>describe の応答をパースする（純関数・テスト対象）。</summary>
    public static DataIoObjectDescribe ParseDescribe(string fallbackName, JsonElement root)
    {
        var name = GetString(root, "name") ?? fallbackName;
        var label = GetString(root, "label") ?? name;
        var fields = new List<DataIoField>();

        if (root.TryGetProperty("fields", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in array.EnumerateArray())
            {
                var fieldName = GetString(field, "name");
                if (string.IsNullOrEmpty(fieldName))
                {
                    continue;
                }

                var referenceTo = field.TryGetProperty("referenceTo", out var refs) && refs.ValueKind == JsonValueKind.Array
                    ? refs.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                    : (IReadOnlyList<string>)Array.Empty<string>();

                fields.Add(new DataIoField(
                    fieldName,
                    GetString(field, "label") ?? fieldName,
                    GetString(field, "type") ?? "string",
                    GetBool(field, "createable"),
                    GetBool(field, "updateable"),
                    GetBool(field, "nillable"),
                    GetBool(field, "defaultedOnCreate"),
                    GetBool(field, "externalId"),
                    GetBool(field, "custom"),
                    referenceTo,
                    GetString(field, "relationshipName"),
                    GetString(field, "calculatedFormula")));
            }
        }

        return new DataIoObjectDescribe(name, label, fields);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.True;
}
