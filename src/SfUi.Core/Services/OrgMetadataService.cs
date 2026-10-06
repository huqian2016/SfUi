using System.Collections.Concurrent;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// org のメタデータ（Apex クラス・カスタムラベル・カスタムメタデータ型）を Tooling API から取得し、
/// 組織単位でメモリ キャッシュする。補完候補（Apex タブ）用。
/// </summary>
public sealed class OrgMetadataService
{
    private readonly SalesforceRestClient _rest;
    private readonly AppLog _log;
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _apexClasses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _customMetadataTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<(string Name, string Label)>> _labels = new(StringComparer.OrdinalIgnoreCase);

    public OrgMetadataService(SalesforceRestClient rest, AppLog log)
    {
        _rest = rest;
        _log = log;
    }

    /// <summary>Apex クラス名一覧を返す（キャッシュ優先）。取得に失敗した場合は空。</summary>
    public async Task<IReadOnlyList<string>> ListApexClassesAsync(string targetOrg, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _apexClasses.TryGetValue(targetOrg, out var cached))
        {
            return cached;
        }

        try
        {
            using var document = await _rest.QueryAsync(
                targetOrg, "SELECT Name FROM ApexClass ORDER BY Name", useToolingApi: true, cancellationToken).ConfigureAwait(false);
            var list = ParseNames(document.RootElement, "Name");
            _apexClasses[targetOrg] = list;
            _log.Info($"Apex クラス一覧を取得: {targetOrg} ({list.Count} 件)");
            return list;
        }
        catch (Exception ex)
        {
            _log.Warn($"Apex クラス一覧の取得に失敗: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>カスタムラベル一覧（System.Label. 用）を返す（キャッシュ優先）。取得に失敗した場合は空。</summary>
    public async Task<IReadOnlyList<(string Name, string Label)>> ListCustomLabelsAsync(string targetOrg, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _labels.TryGetValue(targetOrg, out var cached))
        {
            return cached;
        }

        try
        {
            using var document = await _rest.QueryAsync(
                targetOrg, "SELECT Name, MasterLabel FROM ExternalString ORDER BY Name", useToolingApi: true, cancellationToken).ConfigureAwait(false);
            var list = ParsePairs(document.RootElement, "Name", "MasterLabel");
            _labels[targetOrg] = list;
            _log.Info($"カスタムラベル一覧を取得: {targetOrg} ({list.Count} 件)");
            return list;
        }
        catch (Exception ex)
        {
            _log.Warn($"カスタムラベル一覧の取得に失敗: {ex.Message}");
            return Array.Empty<(string, string)>();
        }
    }

    /// <summary>カスタムメタデータ型（__mdt）の一覧を返す（キャッシュ優先）。取得に失敗した場合は空。</summary>
    public async Task<IReadOnlyList<string>> ListCustomMetadataTypesAsync(string targetOrg, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _customMetadataTypes.TryGetValue(targetOrg, out var cached))
        {
            return cached;
        }

        try
        {
            using var document = await _rest.QueryAsync(
                targetOrg,
                "SELECT QualifiedApiName FROM EntityDefinition WHERE QualifiedApiName LIKE '%__mdt' ORDER BY QualifiedApiName",
                useToolingApi: true,
                cancellationToken).ConfigureAwait(false);
            var list = ParseNames(document.RootElement, "QualifiedApiName");
            _customMetadataTypes[targetOrg] = list;
            _log.Info($"カスタムメタデータ型一覧を取得: {targetOrg} ({list.Count} 件)");
            return list;
        }
        catch (Exception ex)
        {
            _log.Warn($"カスタムメタデータ型一覧の取得に失敗: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>キャッシュを破棄する（組織の再読込用）。</summary>
    public void Invalidate(string targetOrg)
    {
        _apexClasses.TryRemove(targetOrg, out _);
        _customMetadataTypes.TryRemove(targetOrg, out _);
        _labels.TryRemove(targetOrg, out _);
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
