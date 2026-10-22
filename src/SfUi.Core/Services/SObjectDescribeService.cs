using System.Collections.Concurrent;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// オブジェクト一覧（DescribeGlobal）と項目メタデータ（describe）を取得する。
/// メモリ → ディスク キャッシュ（即時応答）→ 裏での最新確認（変更時のみ更新）の順で応答する。
/// 補完候補を起動直後から高速に出すための仕組み。
/// </summary>
public sealed class SObjectDescribeService
{
    private readonly AppLog _log;
    private readonly IMetadataCache? _cache;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<DataIoObject>>> _fetchObjects;
    private readonly Func<string, string, CancellationToken, Task<DataIoObjectDescribe>> _fetchDescribe;

    private readonly ConcurrentDictionary<string, IReadOnlyList<DataIoObject>> _objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DataIoObjectDescribe> _describes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<DataIoObject>>> _objectsInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<DataIoObjectDescribe>> _describesInFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>オブジェクト一覧が最新化されたとき（初回取得・裏の更新で変化あり）に発火する。</summary>
    public event Action<string>? ObjectsChanged;

    public SObjectDescribeService(SalesforceRestClient rest, AppLog log, IMetadataCache? cache = null)
        : this(
            log,
            cache,
            async (org, ct) =>
            {
                using var document = await rest.DescribeGlobalAsync(org, ct).ConfigureAwait(false);
                return ParseObjects(document.RootElement);
            },
            async (org, objectName, ct) =>
            {
                using var document = await rest.DescribeAsync(org, objectName, ct).ConfigureAwait(false);
                return ParseDescribe(objectName, document.RootElement);
            })
    {
    }

    /// <summary>テスト用（REST 呼び出しを差し替える）。</summary>
    internal SObjectDescribeService(
        AppLog log,
        IMetadataCache? cache,
        Func<string, CancellationToken, Task<IReadOnlyList<DataIoObject>>> fetchObjects,
        Func<string, string, CancellationToken, Task<DataIoObjectDescribe>> fetchDescribe)
    {
        _log = log;
        _cache = cache;
        _fetchObjects = fetchObjects;
        _fetchDescribe = fetchDescribe;
    }

    /// <summary>組織の sObject 一覧を返す（メモリ → ディスク キャッシュ優先）。</summary>
    public async Task<IReadOnlyList<DataIoObject>> ListObjectsAsync(string targetOrg, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _objects.TryGetValue(targetOrg, out var cached))
        {
            return cached;
        }

        if (!forceRefresh && _cache is not null && _cache.TryGetObjects(targetOrg, out var disk))
        {
            _objects[targetOrg] = disk;
            _log.Info($"オブジェクト一覧をキャッシュから復元: {targetOrg} ({disk.Count} 件)");
            ObserveBackground(GetOrStartObjectsFetch(targetOrg), $"オブジェクト一覧の背景更新に失敗: {targetOrg}");
            return disk;
        }

        return await GetOrStartObjectsFetch(targetOrg).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>オブジェクトの項目メタデータを返す（メモリ → ディスク キャッシュ優先）。</summary>
    public async Task<DataIoObjectDescribe> DescribeAsync(string targetOrg, string objectName, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var key = targetOrg + "\n" + objectName;
        if (!forceRefresh && _describes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (!forceRefresh && _cache is not null && _cache.TryGetDescribe(targetOrg, objectName, out var disk))
        {
            _describes[key] = disk;
            ObserveBackground(GetOrStartDescribeFetch(targetOrg, objectName), $"describe の背景更新に失敗: {objectName}");
            return disk;
        }

        return await GetOrStartDescribeFetch(targetOrg, objectName).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>キャッシュを破棄する（オブジェクト再読込用。次回アクセス時に取得し直す）。</summary>
    public void Invalidate(string targetOrg)
    {
        _objects.TryRemove(targetOrg, out _);
        foreach (var key in _describes.Keys.Where(k => k.StartsWith(targetOrg + "\n", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _describes.TryRemove(key, out _);
        }

        _cache?.Remove(targetOrg);
    }

    private Task<IReadOnlyList<DataIoObject>> GetOrStartObjectsFetch(string targetOrg) =>
        _objectsInFlight.GetOrAdd(targetOrg, key =>
        {
            var task = FetchAndStoreObjectsAsync(key);
            _ = task.ContinueWith(_ => _objectsInFlight.TryRemove(key, out _), TaskScheduler.Default);
            return task;
        });

    /// <summary>背景更新（テスト用。実行中の取得があれば合流する）。</summary>
    internal Task<IReadOnlyList<DataIoObject>> RefreshObjectsAsync(string targetOrg) =>
        GetOrStartObjectsFetch(targetOrg);

    private async Task<IReadOnlyList<DataIoObject>> FetchAndStoreObjectsAsync(string targetOrg)
    {
        var list = await _fetchObjects(targetOrg, CancellationToken.None).ConfigureAwait(false);
        var changed = !_objects.TryGetValue(targetOrg, out var previous)
            || !MetadataCacheComparison.ObjectsEqual(previous, list);

        _objects[targetOrg] = list;

        if (changed)
        {
            _cache?.SetObjects(targetOrg, list);
            _log.Info($"オブジェクト一覧を取得: {targetOrg} ({list.Count} 件)");
            ObjectsChanged?.Invoke(targetOrg);
        }
        else
        {
            _log.Info($"オブジェクト一覧は最新でした: {targetOrg} ({list.Count} 件)");
        }

        return list;
    }

    private Task<DataIoObjectDescribe> GetOrStartDescribeFetch(string targetOrg, string objectName)
    {
        var key = targetOrg + "\n" + objectName;
        return _describesInFlight.GetOrAdd(key, k =>
        {
            var task = FetchAndStoreDescribeAsync(targetOrg, objectName);
            _ = task.ContinueWith(_ => _describesInFlight.TryRemove(k, out _), TaskScheduler.Default);
            return task;
        });
    }

    /// <summary>背景更新（テスト用。実行中の取得があれば合流する）。</summary>
    internal Task<DataIoObjectDescribe> RefreshDescribeAsync(string targetOrg, string objectName) =>
        GetOrStartDescribeFetch(targetOrg, objectName);

    private async Task<DataIoObjectDescribe> FetchAndStoreDescribeAsync(string targetOrg, string objectName)
    {
        var describe = await _fetchDescribe(targetOrg, objectName, CancellationToken.None).ConfigureAwait(false);
        var key = targetOrg + "\n" + objectName;
        var changed = !_describes.TryGetValue(key, out var previous)
            || !MetadataCacheComparison.DescribesEqual(previous, describe);

        _describes[key] = describe;

        if (changed)
        {
            _cache?.SetDescribe(targetOrg, objectName, describe);
            _log.Info($"describe を取得: {targetOrg} / {objectName} ({describe.Fields.Count} 項目)");
        }

        return describe;
    }

    /// <summary>背景タスクの失敗をログに残す（未監視例外を握りつぶさない）。</summary>
    private void ObserveBackground(Task task, string message)
    {
        _ = task.ContinueWith(
            t => _log.Warn($"{message}: {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
