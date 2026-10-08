using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SfUi.Core;

/// <summary>
/// 項目の使用箇所（フィールド影響分析）。
/// Apex / フロー / 入力規則 / レイアウト / 数式項目 / 項目権限を横断検索する。
/// ソースごとに独立して失敗を許容し（Warnings に記録）、部分結果を返す。
/// </summary>
public sealed class FieldUsageService
{
    /// <summary>1 コンポーネントあたりの最大ヒット数。</summary>
    public const int MaxHitsPerComponent = 20;

    /// <summary>フロー メタデータの 1 回のスキャンで取得する最大数（多すぎる組織の保護）。</summary>
    private const int MaxFlowFetches = 400;

    /// <summary>並列 API 呼び出し数。</summary>
    private const int ParallelFetches = 6;

    private const int MaxRecordsPerSource = 5000;

    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly SalesforceRestClient _rest;
    private readonly SObjectDescribeService _describe;
    private readonly AppLog _log;

    private readonly ConcurrentDictionary<string, IReadOnlyList<ApexSource>> _apexCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<MetadataSource>> _flowCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<MetadataSource>> _validationRuleCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<MetadataSource>> _layoutCache = new(StringComparer.OrdinalIgnoreCase);

    public FieldUsageService(SalesforceRestClient rest, SObjectDescribeService describe, AppLog log)
    {
        _rest = rest;
        _describe = describe;
        _log = log;
    }

    /// <summary>使用箇所をまとめて検索する（ソース単位で部分失敗を許容）。</summary>
    public async Task<FieldUsageResult> AnalyzeAsync(
        string targetOrg,
        string objectApiName,
        string fieldApiName,
        string? instanceUrl = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var hits = new ConcurrentBag<FieldUsageHit>();
        var warnings = new ConcurrentBag<FieldUsageWarning>();

        await Task.WhenAll(
            ScanApexAsync(targetOrg, fieldApiName, instanceUrl, forceRefresh, hits, warnings, cancellationToken),
            ScanFlowsAsync(targetOrg, fieldApiName, instanceUrl, forceRefresh, hits, warnings, cancellationToken),
            ScanValidationRulesAsync(targetOrg, objectApiName, fieldApiName, forceRefresh, hits, warnings, cancellationToken),
            ScanLayoutsAsync(targetOrg, objectApiName, fieldApiName, instanceUrl, forceRefresh, hits, warnings, cancellationToken),
            ScanFormulaFieldsAsync(targetOrg, objectApiName, fieldApiName, forceRefresh, hits, warnings, cancellationToken),
            ScanPermissionsAsync(targetOrg, objectApiName, fieldApiName, hits, warnings, cancellationToken))
            .ConfigureAwait(false);

        stopwatch.Stop();
        _log.Info($"フィールド使用箇所: {targetOrg} {objectApiName}.{fieldApiName} = {hits.Count} 件（{stopwatch.Elapsed.TotalSeconds:F1} 秒 / 警告 {warnings.Count}）");

        return new FieldUsageResult
        {
            ObjectApiName = objectApiName,
            FieldApiName = fieldApiName,
            Hits = hits
                .OrderBy(h => h.SourceKind)
                .ThenBy(h => h.ComponentName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(h => h.LineNumber ?? 0)
                .ThenBy(h => h.Path, StringComparer.Ordinal)
                .ToList(),
            Warnings = warnings
                .OrderBy(w => w.SourceKind)
                .ThenBy(w => w.Message, StringComparer.Ordinal)
                .ToList(),
            Duration = stopwatch.Elapsed,
        };
    }

    /// <summary>項目 API 名の完全一致トークン（単語境界）を作る。例: MyField__c は MyField__cX に一致しない。</summary>
    public static Regex BuildFieldRegex(string fieldApiName) =>
        RegexCache.GetOrAdd(fieldApiName, static field => new Regex(
            $@"(?<![A-Za-z0-9_]){Regex.Escape(field)}(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled));

    /// <summary>テキスト（Apex など）を行単位でスキャンする（純関数・テスト対象）。</summary>
    public static IReadOnlyList<FieldUsageHit> ScanText(
        string text,
        string fieldApiName,
        string componentName,
        FieldUsageSourceKind kind,
        string? componentId = null,
        string? openUrl = null,
        int maxHitsPerComponent = MaxHitsPerComponent,
        bool skipCommentLines = false)
    {
        var hits = new List<FieldUsageHit>();
        if (string.IsNullOrEmpty(text))
        {
            return hits;
        }

        var regex = BuildFieldRegex(fieldApiName);
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (skipCommentLines && (trimmed.StartsWith('*') || trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal)))
            {
                // コメント行は使用箇所として扱わない（Apex のノイズ削減）
                continue;
            }

            if (!regex.IsMatch(line))
            {
                continue;
            }

            hits.Add(new FieldUsageHit
            {
                SourceKind = kind,
                ComponentName = componentName,
                ComponentId = componentId,
                LineNumber = i + 1,
                Excerpt = Truncate(line.Trim(), 200),
                OpenUrl = openUrl,
            });
            if (hits.Count >= maxHitsPerComponent)
            {
                break;
            }
        }

        return hits;
    }

    /// <summary>
    /// メタデータ（JSON / XML 文字列）をスキャンする（純関数・テスト対象）。
    /// JSON は階層パス付きで文字列値を走査し、それ以外はテキストとして周辺文字を抜粋する。
    /// </summary>
    public static IReadOnlyList<FieldUsageHit> ScanMetadata(
        string rawMetadata,
        string fieldApiName,
        string componentName,
        FieldUsageSourceKind kind,
        string? componentId = null,
        string? openUrl = null,
        int maxHitsPerComponent = MaxHitsPerComponent)
    {
        var hits = new List<FieldUsageHit>();
        if (string.IsNullOrWhiteSpace(rawMetadata))
        {
            return hits;
        }

        try
        {
            using var document = JsonDocument.Parse(rawMetadata);
            WalkJson(document.RootElement, "", fieldApiName, componentName, kind, componentId, openUrl, hits, maxHitsPerComponent);
            return hits;
        }
        catch (JsonException)
        {
            // XML などのプレーンテキスト（入力規則の XML など）
            var regex = BuildFieldRegex(fieldApiName);
            foreach (Match match in regex.Matches(rawMetadata))
            {
                var start = Math.Max(0, match.Index - 80);
                var length = Math.Min(rawMetadata.Length - start, match.Length + 160);
                hits.Add(new FieldUsageHit
                {
                    SourceKind = kind,
                    ComponentName = componentName,
                    ComponentId = componentId,
                    Excerpt = Truncate(rawMetadata.Substring(start, length).Trim(), 200),
                    OpenUrl = openUrl,
                });
                if (hits.Count >= maxHitsPerComponent)
                {
                    break;
                }
            }

            return hits;
        }
    }

    private static bool WalkJson(
        JsonElement element,
        string path,
        string field,
        string componentName,
        FieldUsageSourceKind kind,
        string? componentId,
        string? openUrl,
        List<FieldUsageHit> hits,
        int maxHits)
    {
        if (hits.Count >= maxHits)
        {
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = path.Length == 0 ? property.Name : path + "." + property.Name;
                    if (!WalkJson(property.Value, childPath, field, componentName, kind, componentId, openUrl, hits, maxHits))
                    {
                        return false;
                    }
                }

                return true;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (!WalkJson(item, $"{path}[{index}]", field, componentName, kind, componentId, openUrl, hits, maxHits))
                    {
                        return false;
                    }

                    index++;
                }

                return true;

            case JsonValueKind.String:
                var value = element.GetString() ?? "";
                if (BuildFieldRegex(field).IsMatch(value))
                {
                    hits.Add(new FieldUsageHit
                    {
                        SourceKind = kind,
                        ComponentName = componentName,
                        ComponentId = componentId,
                        Path = path,
                        Excerpt = Truncate(value, 200),
                        OpenUrl = openUrl,
                    });
                    if (hits.Count >= maxHits)
                    {
                        return false;
                    }
                }

                return true;

            default:
                return true;
        }
    }

    // ---- Apex ----

    private async Task ScanApexAsync(
        string targetOrg,
        string fieldApiName,
        string? instanceUrl,
        bool forceRefresh,
        ConcurrentBag<FieldUsageHit> hits,
        ConcurrentBag<FieldUsageWarning> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var sources = await GetApexSourcesAsync(targetOrg, forceRefresh, cancellationToken).ConfigureAwait(false);
            foreach (var source in sources)
            {
                var kind = source.IsTrigger ? FieldUsageSourceKind.ApexTrigger : FieldUsageSourceKind.ApexClass;
                var url = BuildSetupUrl(instanceUrl, source.IsTrigger ? "ApexTriggers" : "ApexClasses", source.Id);
                foreach (var hit in ScanText(source.Body, fieldApiName, source.Name, kind, source.Id, url, skipCommentLines: true))
                {
                    hits.Add(hit);
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add(new FieldUsageWarning(FieldUsageSourceKind.ApexClass, ex.Message));
            _log.Error("Apex の使用箇所スキャンに失敗", ex);
        }
    }

    private async Task<IReadOnlyList<ApexSource>> GetApexSourcesAsync(string targetOrg, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _apexCache.TryGetValue(targetOrg, out var cached))
        {
            return cached;
        }

        var list = new List<ApexSource>();
        await QueryAllPagesAsync(
            targetOrg,
            "SELECT Id, Name, Body FROM ApexClass WHERE NamespacePrefix = null",
            useToolingApi: true,
            record => list.Add(new ApexSource(
                GetString(record, "Id") ?? "",
                GetString(record, "Name") ?? "",
                GetString(record, "Body") ?? "",
                false)),
            cancellationToken).ConfigureAwait(false);

        await QueryAllPagesAsync(
            targetOrg,
            "SELECT Id, Name, Body FROM ApexTrigger WHERE NamespacePrefix = null",
            useToolingApi: true,
            record => list.Add(new ApexSource(
                GetString(record, "Id") ?? "",
                GetString(record, "Name") ?? "",
                GetString(record, "Body") ?? "",
                true)),
            cancellationToken).ConfigureAwait(false);

        _apexCache[targetOrg] = list;
        return list;
    }

    // ---- フロー ----

    private async Task ScanFlowsAsync(
        string targetOrg,
        string fieldApiName,
        string? instanceUrl,
        bool forceRefresh,
        ConcurrentBag<FieldUsageHit> hits,
        ConcurrentBag<FieldUsageWarning> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var (sources, failedCount) = await GetFlowSourcesAsync(targetOrg, forceRefresh, cancellationToken).ConfigureAwait(false);
            foreach (var source in sources)
            {
                var url = BuildSetupUrl(instanceUrl, "Flows", source.Id);
                foreach (var hit in ScanMetadata(source.RawMetadata, fieldApiName, source.Name, FieldUsageSourceKind.Flow, source.Id, url))
                {
                    hits.Add(hit);
                }
            }

            if (failedCount > 0)
            {
                warnings.Add(new FieldUsageWarning(FieldUsageSourceKind.Flow, $"メタデータを取得できなかったフロー: {failedCount} 件"));
            }
        }
        catch (Exception ex)
        {
            warnings.Add(new FieldUsageWarning(FieldUsageSourceKind.Flow, ex.Message));
            _log.Error("フローの使用箇所スキャンに失敗", ex);
        }
    }

    private async Task<(IReadOnlyList<MetadataSource> Sources, int FailedCount)> GetFlowSourcesAsync(string targetOrg, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _flowCache.TryGetValue(targetOrg, out var cached))
        {
            return (cached, 0);
        }

        // アクティブ バージョンのみを対象とする（Tooling の Flow は Status で絞ると複数行取得できる）
        var rows = new List<(string Id, string Name)>();
        await QueryAllPagesAsync(
            targetOrg,
            "SELECT Id, MasterLabel FROM Flow WHERE Status = 'Active'",
            useToolingApi: true,
            record => rows.Add((GetString(record, "Id") ?? "", GetString(record, "MasterLabel") ?? "")),
            cancellationToken).ConfigureAwait(false);

        var results = new ConcurrentBag<MetadataSource>();
        var failures = 0;
        using var semaphore = new SemaphoreSlim(ParallelFetches);
        var tasks = rows.Take(MaxFlowFetches).Select(async row =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Metadata フィールドは 1 行制限があるため 1 件ずつ取得する
                var raw = await FetchSingleMetadataAsync(
                    targetOrg,
                    $"SELECT Metadata FROM Flow WHERE Id = '{row.Id}'",
                    cancellationToken).ConfigureAwait(false);
                if (raw is not null)
                {
                    results.Add(new MetadataSource(row.Id, string.IsNullOrEmpty(row.Name) ? row.Id : row.Name, raw));
                }
                else
                {
                    Interlocked.Increment(ref failures);
                }
            }
            catch
            {
                Interlocked.Increment(ref failures);
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);

        var list = results.ToList();
        _flowCache[targetOrg] = list;
        return (list, failures);
    }

    // ---- 入力規則 ----

    private async Task ScanValidationRulesAsync(
        string targetOrg,
        string objectApiName,
        string fieldApiName,
        bool forceRefresh,
        ConcurrentBag<FieldUsageHit> hits,
        ConcurrentBag<FieldUsageWarning> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var (sources, failedCount) = await GetValidationRuleSourcesAsync(targetOrg, objectApiName, forceRefresh, cancellationToken).ConfigureAwait(false);
            foreach (var source in sources)
            {
                foreach (var hit in ScanMetadata(source.RawMetadata, fieldApiName, source.Name, FieldUsageSourceKind.ValidationRule, source.Id))
                {
                    hits.Add(hit);
                }
            }

            if (failedCount > 0)
            {
                warnings.Add(new FieldUsageWarning(FieldUsageSourceKind.ValidationRule, $"メタデータを取得できなかった入力規則: {failedCount} 件"));
            }
        }
        catch (Exception ex)
        {
            warnings.Add(new FieldUsageWarning(FieldUsageSourceKind.ValidationRule, ex.Message));
            _log.Error("入力規則の使用箇所スキャンに失敗", ex);
        }
    }

    private async Task<(IReadOnlyList<MetadataSource> Sources, int FailedCount)> GetValidationRuleSourcesAsync(
        string targetOrg,
        string objectApiName,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var key = targetOrg + "\n" + objectApiName;
        if (!forceRefresh && _validationRuleCache.TryGetValue(key, out var cached))
        {
            return (cached, 0);
        }

        var rows = new List<(string Id, string Name)>();
        await QueryAllPagesAsync(
            targetOrg,
            $"SELECT Id, ValidationName FROM ValidationRule WHERE EntityDefinitionId = '{OrgInfoQueryBuilder.EscapeSoqlString(objectApiName)}'",
            useToolingApi: true,
            record => rows.Add((GetString(record, "Id") ?? "", GetString(record, "ValidationName") ?? "")),
            cancellationToken).ConfigureAwait(false);

        var results = new ConcurrentBag<MetadataSource>();
        var failures = 0;
        foreach (var row in rows)
        {
            try
            {
                var raw = await FetchSingleMetadataAsync(
                    targetOrg,
                    $"SELECT Metadata FROM ValidationRule WHERE Id = '{row.Id}'",
                    cancellationToken).ConfigureAwait(false);
                if (raw is not null)
                {
                    results.Add(new MetadataSource(row.Id, string.IsNullOrEmpty(row.Name) ? row.Id : row.Name, raw));
                }
                else
                {
                    failures++;
                }
            }
            catch
            {
                failures++;
            }
        }

        var list = results.ToList();
        _validationRuleCache[key] = list;
        return (list, failures);
    }

    // ---- レイアウト ----

    private async Task ScanLayoutsAsync(
        string targetOrg,
        string objectApiName,
        string fieldApiName,
        string? instanceUrl,
        bool forceRefresh,
        ConcurrentBag<FieldUsageHit> hits,
        ConcurrentBag<FieldUsageWarning> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var (sources, failedCount) = await GetLayoutSourcesAsync(targetOrg, objectApiName, forceRefresh, cancellationToken).ConfigureAwait(false);
            foreach (var source in sources)
            {
                var url = BuildSetupUrl(instanceUrl, "ObjectManager", null);
                foreach (var hit in ScanMetadata(source.RawMetadata, fieldApiName, source.Name, FieldUsageSourceKind.Layout, source.Id, url))
                {
                    hits.Add(hit);
                }
            }

            if (failedCount > 0)
            {
                warnings.Add(new FieldUsageWarning(FieldUsageSourceKind.Layout, $"メタデータを取得できなかったレイアウト: {failedCount} 件"));
            }
        }
        catch (Exception ex)
        {
            warnings.Add(new FieldUsageWarning(FieldUsageSourceKind.Layout, ex.Message));
            _log.Error("レイアウトの使用箇所スキャンに失敗", ex);
        }
    }

    private async Task<(IReadOnlyList<MetadataSource> Sources, int FailedCount)> GetLayoutSourcesAsync(
        string targetOrg,
        string objectApiName,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var key = targetOrg + "\n" + objectApiName;
        if (!forceRefresh && _layoutCache.TryGetValue(key, out var cached))
        {
            return (cached, 0);
        }

        // レイアウトは EntityDefinitionId で絞ると複数行を取得できる（無絞り込みは 1 行制限）
        var rows = new List<(string Id, string Name)>();
        await QueryAllPagesAsync(
            targetOrg,
            $"SELECT Id, Name FROM Layout WHERE EntityDefinitionId = '{OrgInfoQueryBuilder.EscapeSoqlString(objectApiName)}'",
            useToolingApi: true,
            record => rows.Add((GetString(record, "Id") ?? "", GetString(record, "Name") ?? "")),
            cancellationToken).ConfigureAwait(false);

        var results = new ConcurrentBag<MetadataSource>();
        var failures = 0;
        using var semaphore = new SemaphoreSlim(ParallelFetches);
        var tasks = rows.Select(async row =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var raw = await FetchSingleMetadataAsync(
                    targetOrg,
                    $"SELECT Metadata FROM Layout WHERE Id = '{row.Id}'",
                    cancellationToken).ConfigureAwait(false);
                if (raw is not null)
                {
                    results.Add(new MetadataSource(row.Id, string.IsNullOrEmpty(row.Name) ? row.Id : row.Name, raw));
                }
                else
                {
                    Interlocked.Increment(ref failures);
                }
            }
            catch
            {
                Interlocked.Increment(ref failures);
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);

        var list = results.ToList();
        _layoutCache[key] = list;
        return (list, failures);
    }

    // ---- 数式項目 ----

    private async Task ScanFormulaFieldsAsync(
        string targetOrg,
        string objectApiName,
        string fieldApiName,
        bool forceRefresh,
        ConcurrentBag<FieldUsageHit> hits,
        ConcurrentBag<FieldUsageWarning> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var describe = await _describe.DescribeAsync(targetOrg, objectApiName, forceRefresh, cancellationToken).ConfigureAwait(false);
            var regex = BuildFieldRegex(fieldApiName);
            foreach (var field in describe.Fields)
            {
                var formula = field.CalculatedFormula;
                if (string.IsNullOrWhiteSpace(formula))
                {
                    continue;
                }

                if (string.Equals(field.Name, fieldApiName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!regex.IsMatch(formula))
                {
                    continue;
                }

                hits.Add(new FieldUsageHit
                {
                    SourceKind = FieldUsageSourceKind.FormulaField,
                    ComponentName = string.Equals(field.Label, field.Name, StringComparison.Ordinal) ? field.Name : $"{field.Label} ({field.Name})",
                    ComponentId = field.Name,
                    Excerpt = Truncate(formula.Trim(), 200),
                });
            }
        }
        catch (Exception ex)
        {
            warnings.Add(new FieldUsageWarning(FieldUsageSourceKind.FormulaField, ex.Message));
            _log.Error("数式項目の使用箇所スキャンに失敗", ex);
        }
    }

    // ---- 項目権限 ----

    private async Task ScanPermissionsAsync(
        string targetOrg,
        string objectApiName,
        string fieldApiName,
        ConcurrentBag<FieldUsageHit> hits,
        ConcurrentBag<FieldUsageWarning> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            // 主体（プロファイル / 権限セット / 権限セット グループ）の ID → 表示名
            var subjectNames = new Dictionary<string, string>(StringComparer.Ordinal);
            await QueryAllPagesAsync(
                targetOrg,
                "SELECT Id, Name, Label, IsOwnedByProfile, Profile.Name FROM PermissionSet",
                useToolingApi: false,
                record =>
                {
                    var id = GetString(record, "Id");
                    if (string.IsNullOrEmpty(id))
                    {
                        return;
                    }

                    subjectNames[id] = GetBool(record, "IsOwnedByProfile")
                        ? GetNestedString(record, "Profile", "Name") ?? GetString(record, "Label") ?? id
                        : GetString(record, "Label") ?? GetString(record, "Name") ?? id;
                },
                cancellationToken).ConfigureAwait(false);

            await QueryAllPagesAsync(
                targetOrg,
                "SELECT Id, MasterLabel FROM PermissionSetGroup",
                useToolingApi: false,
                record =>
                {
                    var id = GetString(record, "Id");
                    if (!string.IsNullOrEmpty(id))
                    {
                        subjectNames[id] = GetString(record, "MasterLabel") ?? id;
                    }
                },
                cancellationToken).ConfigureAwait(false);

            // FieldPermissions.Field は WHERE で照合できない（0 件になる）ため、SobjectType で取得してクライアント側で照合する
            var targetField = objectApiName + "." + fieldApiName;
            await QueryAllPagesAsync(
                targetOrg,
                $"SELECT ParentId, Field, PermissionsRead, PermissionsEdit FROM FieldPermissions WHERE SobjectType = '{OrgInfoQueryBuilder.EscapeSoqlString(objectApiName)}'",
                useToolingApi: false,
                record =>
                {
                    var field = GetString(record, "Field");
                    if (!string.Equals(field, targetField, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    var read = GetBool(record, "PermissionsRead");
                    var edit = GetBool(record, "PermissionsEdit");
                    if (!read && !edit)
                    {
                        return;
                    }

                    var parentId = GetString(record, "ParentId") ?? "";
                    hits.Add(new FieldUsageHit
                    {
                        SourceKind = FieldUsageSourceKind.Permission,
                        ComponentName = subjectNames.TryGetValue(parentId, out var name) ? name : parentId,
                        ComponentId = parentId,
                        Excerpt = $"Read: {read} / Edit: {edit}",
                    });
                },
                cancellationToken,
                maxRecords: 30000).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            warnings.Add(new FieldUsageWarning(FieldUsageSourceKind.Permission, ex.Message));
            _log.Error("項目権限のスキャンに失敗", ex);
        }
    }

    // ---- 共通 ----

    private async Task QueryAllPagesAsync(
        string targetOrg,
        string soql,
        bool useToolingApi,
        Action<JsonElement> onRecord,
        CancellationToken cancellationToken,
        int maxRecords = MaxRecordsPerSource)
    {
        using var first = await _rest.QueryAsync(targetOrg, soql, useToolingApi, cancellationToken).ConfigureAwait(false);
        var count = AppendRecords(first.RootElement, onRecord);
        var (done, next) = ReadPaging(first.RootElement);
        while (!done && !string.IsNullOrEmpty(next) && count < maxRecords)
        {
            using var page = await _rest.GetPageAsync(targetOrg, next!, cancellationToken).ConfigureAwait(false);
            count += AppendRecords(page.RootElement, onRecord);
            (done, next) = ReadPaging(page.RootElement);
        }
    }

    private static int AppendRecords(JsonElement root, Action<JsonElement> onRecord)
    {
        var count = 0;
        if (root.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in records.EnumerateArray())
            {
                onRecord(record);
                count++;
            }
        }

        return count;
    }

    private static (bool Done, string? Next) ReadPaging(JsonElement root)
    {
        var done = !(root.TryGetProperty("done", out var doneElement) && doneElement.ValueKind == JsonValueKind.False);
        var next = root.TryGetProperty("nextRecordsUrl", out var nextElement) && nextElement.ValueKind == JsonValueKind.String
            ? nextElement.GetString()
            : null;
        return (done, next);
    }

    private async Task<string?> FetchSingleMetadataAsync(string targetOrg, string soql, CancellationToken cancellationToken)
    {
        using var document = await _rest.QueryAsync(targetOrg, soql, useToolingApi: true, cancellationToken).ConfigureAwait(false);
        if (document.RootElement.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in records.EnumerateArray())
            {
                if (record.TryGetProperty("Metadata", out var metadata) && metadata.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                {
                    return metadata.GetRawText();
                }
            }
        }

        return null;
    }

    private static string? BuildSetupUrl(string? instanceUrl, string section, string? id)
    {
        if (string.IsNullOrWhiteSpace(instanceUrl) || string.IsNullOrEmpty(id))
        {
            return null;
        }

        return $"{instanceUrl.TrimEnd('/')}/lightning/setup/{section}/page?address=/{id}";
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetNestedString(JsonElement element, string parent, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(parent, out var parentElement)
        && parentElement.ValueKind == JsonValueKind.Object
            ? GetString(parentElement, name)
            : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private sealed record ApexSource(string Id, string Name, string Body, bool IsTrigger);

    private sealed record MetadataSource(string Id, string Name, string RawMetadata);
}
