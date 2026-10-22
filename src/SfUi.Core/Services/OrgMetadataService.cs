using System.Collections.Concurrent;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// org のメタデータ（Apex クラス・カスタムラベル・カスタムメタデータ型）を Tooling API から取得する。
/// メモリ → ディスク キャッシュ（即時応答）→ 裏での最新確認（変更時のみ更新）の順で応答する。
/// </summary>
public sealed class OrgMetadataService
{
    private readonly AppLog _log;
    private readonly IMetadataCache? _cache;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _fetchApexClasses;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<(string Name, string Label)>>> _fetchLabels;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _fetchCustomMetadataTypes;

    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _apexClasses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<(string Name, string Label)>> _labels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _customMetadataTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<string>>> _apexClassesInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<(string, string)>>> _labelsInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<string>>> _customMetadataTypesInFlight = new(StringComparer.OrdinalIgnoreCase);

    public OrgMetadataService(SalesforceRestClient rest, AppLog log, IMetadataCache? cache = null)
        : this(
            log,
            cache,
            async (org, ct) =>
            {
                using var document = await rest.QueryAsync(
                    org, "SELECT Name FROM ApexClass ORDER BY Name", useToolingApi: true, ct).ConfigureAwait(false);
                return ParseNames(document.RootElement, "Name");
            },
            async (org, ct) =>
            {
                using var document = await rest.QueryAsync(
                    org, "SELECT Name, MasterLabel FROM ExternalString ORDER BY Name", useToolingApi: true, ct).ConfigureAwait(false);
                return ParsePairs(document.RootElement, "Name", "MasterLabel");
            },
            async (org, ct) =>
            {
                using var document = await rest.QueryAsync(
                    org,
                    "SELECT QualifiedApiName FROM EntityDefinition WHERE QualifiedApiName LIKE '%__mdt' ORDER BY QualifiedApiName",
                    useToolingApi: true,
                    ct).ConfigureAwait(false);
                return ParseNames(document.RootElement, "QualifiedApiName");
            })
    {
    }

    /// <summary>テスト用（Tooling クエリを差し替える）。</summary>
    internal OrgMetadataService(
        AppLog log,
        IMetadataCache? cache,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> fetchApexClasses,
        Func<string, CancellationToken, Task<IReadOnlyList<(string Name, string Label)>>> fetchLabels,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> fetchCustomMetadataTypes)
    {
        _log = log;
        _cache = cache;
        _fetchApexClasses = fetchApexClasses;
        _fetchLabels = fetchLabels;
        _fetchCustomMetadataTypes = fetchCustomMetadataTypes;
    }

    /// <summary>Apex クラス名一覧を返す（メモリ → ディスク キャッシュ優先。取得に失敗した場合は空）。</summary>
    public async Task<IReadOnlyList<string>> ListApexClassesAsync(string targetOrg, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _apexClasses.TryGetValue(targetOrg, out var cached))
        {
            return cached;
        }

        if (!forceRefresh && _cache is not null && _cache.TryGetApexClasses(targetOrg, out var disk))
        {
            _apexClasses[targetOrg] = disk;
            _log.Info($"Apex クラス一覧をキャッシュから復元: {targetOrg} ({disk.Count} 件)");
            ObserveBackground(GetOrStartApexClassesFetch(targetOrg), $"Apex クラス一覧の背景更新に失敗: {targetOrg}");
            return disk;
        }

        return await GetOrStartApexClassesFetch(targetOrg).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>カスタムラベル一覧（System.Label. 用）を返す（メモリ → ディスク キャッシュ優先。取得に失敗した場合は空）。</summary>
    public async Task<IReadOnlyList<(string Name, string Label)>> ListCustomLabelsAsync(string targetOrg, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _labels.TryGetValue(targetOrg, out var cached))
        {
            return cached;
        }

        if (!forceRefresh && _cache is not null && _cache.TryGetLabels(targetOrg, out var disk))
        {
            _labels[targetOrg] = disk;
            _log.Info($"カスタムラベル一覧をキャッシュから復元: {targetOrg} ({disk.Count} 件)");
            ObserveBackground(GetOrStartLabelsFetch(targetOrg), $"カスタムラベル一覧の背景更新に失敗: {targetOrg}");
            return disk;
        }

        return await GetOrStartLabelsFetch(targetOrg).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>カスタムメタデータ型（__mdt）の一覧を返す（メモリ → ディスク キャッシュ優先。取得に失敗した場合は空）。</summary>
    public async Task<IReadOnlyList<string>> ListCustomMetadataTypesAsync(string targetOrg, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _customMetadataTypes.TryGetValue(targetOrg, out var cached))
        {
            return cached;
        }

        if (!forceRefresh && _cache is not null && _cache.TryGetCustomMetadataTypes(targetOrg, out var disk))
        {
            _customMetadataTypes[targetOrg] = disk;
            _log.Info($"カスタムメタデータ型一覧をキャッシュから復元: {targetOrg} ({disk.Count} 件)");
            ObserveBackground(GetOrStartCustomMetadataTypesFetch(targetOrg), $"カスタムメタデータ型一覧の背景更新に失敗: {targetOrg}");
            return disk;
        }

        return await GetOrStartCustomMetadataTypesFetch(targetOrg).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>キャッシュを破棄する（組織の再読込用）。</summary>
    public void Invalidate(string targetOrg)
    {
        _apexClasses.TryRemove(targetOrg, out _);
        _customMetadataTypes.TryRemove(targetOrg, out _);
        _labels.TryRemove(targetOrg, out _);
        _cache?.Remove(targetOrg);
    }

    /// <summary>背景更新（テスト用。実行中の取得があれば合流する）。</summary>
    internal Task<IReadOnlyList<string>> RefreshApexClassesAsync(string targetOrg) =>
        GetOrStartApexClassesFetch(targetOrg);

    /// <summary>背景更新（テスト用）。</summary>
    internal Task<IReadOnlyList<(string Name, string Label)>> RefreshLabelsAsync(string targetOrg) =>
        GetOrStartLabelsFetch(targetOrg);

    /// <summary>背景更新（テスト用）。</summary>
    internal Task<IReadOnlyList<string>> RefreshCustomMetadataTypesAsync(string targetOrg) =>
        GetOrStartCustomMetadataTypesFetch(targetOrg);

    private Task<IReadOnlyList<string>> GetOrStartApexClassesFetch(string targetOrg) =>
        _apexClassesInFlight.GetOrAdd(targetOrg, key =>
        {
            var task = FetchAndStoreApexClassesAsync(key);
            _ = task.ContinueWith(_ => _apexClassesInFlight.TryRemove(key, out _), TaskScheduler.Default);
            return task;
        });

    private async Task<IReadOnlyList<string>> FetchAndStoreApexClassesAsync(string targetOrg)
    {
        try
        {
            var list = await _fetchApexClasses(targetOrg, CancellationToken.None).ConfigureAwait(false);
            var changed = !_apexClasses.TryGetValue(targetOrg, out var previous)
                || !MetadataCacheComparison.SequenceEqual(previous, list);

            _apexClasses[targetOrg] = list;

            if (changed)
            {
                _cache?.SetApexClasses(targetOrg, list);
                _log.Info($"Apex クラス一覧を取得: {targetOrg} ({list.Count} 件)");
            }

            return list;
        }
        catch (Exception ex)
        {
            _log.Warn($"Apex クラス一覧の取得に失敗: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private Task<IReadOnlyList<(string Name, string Label)>> GetOrStartLabelsFetch(string targetOrg) =>
        _labelsInFlight.GetOrAdd(targetOrg, key =>
        {
            var task = FetchAndStoreLabelsAsync(key);
            _ = task.ContinueWith(_ => _labelsInFlight.TryRemove(key, out _), TaskScheduler.Default);
            return task;
        });

    private async Task<IReadOnlyList<(string Name, string Label)>> FetchAndStoreLabelsAsync(string targetOrg)
    {
        try
        {
            var list = await _fetchLabels(targetOrg, CancellationToken.None).ConfigureAwait(false);
            var changed = !_labels.TryGetValue(targetOrg, out var previous)
                || !MetadataCacheComparison.LabelsEqual(previous, list);

            _labels[targetOrg] = list;

            if (changed)
            {
                _cache?.SetLabels(targetOrg, list);
                _log.Info($"カスタムラベル一覧を取得: {targetOrg} ({list.Count} 件)");
            }

            return list;
        }
        catch (Exception ex)
        {
            _log.Warn($"カスタムラベル一覧の取得に失敗: {ex.Message}");
            return Array.Empty<(string, string)>();
        }
    }

    private Task<IReadOnlyList<string>> GetOrStartCustomMetadataTypesFetch(string targetOrg) =>
        _customMetadataTypesInFlight.GetOrAdd(targetOrg, key =>
        {
            var task = FetchAndStoreCustomMetadataTypesAsync(key);
            _ = task.ContinueWith(_ => _customMetadataTypesInFlight.TryRemove(key, out _), TaskScheduler.Default);
            return task;
        });

    private async Task<IReadOnlyList<string>> FetchAndStoreCustomMetadataTypesAsync(string targetOrg)
    {
        try
        {
            var list = await _fetchCustomMetadataTypes(targetOrg, CancellationToken.None).ConfigureAwait(false);
            var changed = !_customMetadataTypes.TryGetValue(targetOrg, out var previous)
                || !MetadataCacheComparison.SequenceEqual(previous, list);

            _customMetadataTypes[targetOrg] = list;

            if (changed)
            {
                _cache?.SetCustomMetadataTypes(targetOrg, list);
                _log.Info($"カスタムメタデータ型一覧を取得: {targetOrg} ({list.Count} 件)");
            }

            return list;
        }
        catch (Exception ex)
        {
            _log.Warn($"カスタムメタデータ型一覧の取得に失敗: {ex.Message}");
            return Array.Empty<string>();
        }
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


    /// <summary>Tooling クエリ応答から単一プロパティの一覧を取り出す（純関数・テスト対象）。</summary>
    public static IReadOnlyList<string> ParseNames(JsonElement root, string property)
    {
        var list = new List<string>();
        if (!root.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in records.EnumerateArray())
        {
            if (GetString(item, property) is { Length: > 0 } value)
            {
                list.Add(value);
            }
        }

        return list;
    }

    /// <summary>Tooling クエリ応答から（名前, ラベル）の一覧を取り出す（純関数・テスト対象）。</summary>
    public static IReadOnlyList<(string Name, string Label)> ParsePairs(JsonElement root, string nameProperty, string labelProperty)
    {
        var list = new List<(string, string)>();
        if (!root.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in records.EnumerateArray())
        {
            var name = GetString(item, nameProperty);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            list.Add((name, GetString(item, labelProperty) ?? name));
        }

        return list;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
