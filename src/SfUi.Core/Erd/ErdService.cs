using System.Text.Json;

namespace SfUi.Core;

/// <summary>ER 図の対象にできる sObject。</summary>
public sealed record ErdObjectInfo(string ApiName, string Label, bool IsCustom, bool IsCustomSetting)
{
    public bool IsSystemObject => ErdService.IsSystemObject(ApiName, IsCustomSetting);
}

/// <summary>参照項目 1 件（FieldDefinition の 1 行）。</summary>
public sealed record ErdFieldReference(
    string ObjectApiName,
    string FieldApiName,
    string FieldLabel,
    string DataType,
    IReadOnlyList<string> ReferenceTo,
    bool IsNillable)
{
    public bool IsMasterDetail => DataType.Contains("MasterDetail", StringComparison.OrdinalIgnoreCase)
        || DataType.Contains("Master-Detail", StringComparison.OrdinalIgnoreCase);
}

/// <summary>ER 図を組み立てるときのオプション。</summary>
public sealed class ErdBuildOptions
{
    public ErdNameStyle NameStyle { get; init; } = ErdNameStyle.LabelAndApiName;

    /// <summary>標準オブジェクトを含めるか。</summary>
    public bool IncludeStandardObjects { get; init; } = true;

    /// <summary>参照先のオブジェクトが未選択でも自動で追加するか。</summary>
    public bool IncludeReferencedParents { get; init; } = true;

    /// <summary>エンティティ数の上限。</summary>
    public int MaxObjects { get; init; } = 80;
}

/// <summary>
/// 組織のメタデータ（Tooling API の EntityDefinition / FieldDefinition）から
/// ER 図のモデルを組み立てる。JSON の解釈と組み立ては純粋関数にしてテスト可能にしている。
/// </summary>
public sealed class ErdService
{
    private const int MaxRows = 20000;
    private const int ObjectsPerQuery = 10;
    private static readonly string[] SystemSuffixes = { "__mdt", "__e", "__history", "__share", "__feed", "__tag", "__ChangeEvent", "__b" };

    private readonly SalesforceRestClient _rest;
    private readonly AppLog _log;

    public ErdService(SalesforceRestClient rest, AppLog log)
    {
        _rest = rest;
        _log = log;
    }

    /// <summary>選択ダイアログ用のオブジェクト一覧。</summary>
    public async Task<IReadOnlyList<ErdObjectInfo>> ListObjectsAsync(string org, CancellationToken cancellationToken = default)
    {
        const string soql =
            "SELECT QualifiedApiName, Label, IsCustomSetting FROM EntityDefinition " +
            "WHERE IsCustomizable = true ORDER BY QualifiedApiName";

        var records = await QueryAllAsync(org, soql, useToolingApi: true, cancellationToken).ConfigureAwait(false);
        var objects = ParseObjects(records)
            .Where(item => !item.IsSystemObject)
            .OrderBy(item => item.ApiName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _log.Info($"ERD: {objects.Count} 個のオブジェクトを取得しました");
        return objects;
    }

    /// <summary>選択したオブジェクトから ER 図を構築する。</summary>
    public async Task<ErdDocument> BuildAsync(
        string org,
        IReadOnlyCollection<string> objectApiNames,
        ErdBuildOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ErdBuildOptions();
        var selection = new HashSet<string>(objectApiNames, StringComparer.OrdinalIgnoreCase);
        if (selection.Count == 0)
        {
            return new ErdDocument
            {
                Title = "ERD",
                Org = org,
                NameStyle = options.NameStyle,
            };
        }

        var objects = await ListObjectsAsync(org, cancellationToken).ConfigureAwait(false);
        var byName = objects.ToDictionary(item => item.ApiName, StringComparer.OrdinalIgnoreCase);

        // 選択 + 参照先（1 ホップ）を取得対象に含める
        var wanted = new HashSet<string>(selection, StringComparer.OrdinalIgnoreCase);
        var references = new List<ErdFieldReference>();
        foreach (var chunk in Chunk(wanted, ObjectsPerQuery))
        {
            references.AddRange(await QueryFieldReferencesAsync(org, chunk, cancellationToken).ConfigureAwait(false));
        }

        if (options.IncludeReferencedParents)
        {
            foreach (var reference in references.Where(reference => wanted.Contains(reference.ObjectApiName)))
            {
                foreach (var parent in reference.ReferenceTo)
                {
                    if (byName.ContainsKey(parent))
                    {
                        wanted.Add(parent);
                    }
                }
            }
        }

        // 追加分の参照項目も取得（参照先オブジェクトの項目を表示するため）
        var extra = wanted.Where(name => !selection.Contains(name)).ToList();
        foreach (var chunk in Chunk(extra, ObjectsPerQuery))
        {
            references.AddRange(await QueryFieldReferencesAsync(org, chunk, cancellationToken).ConfigureAwait(false));
        }

        var scopeObjects = byName.Values
            .Where(item => wanted.Contains(item.ApiName))
            .ToList();

        return Build(scopeObjects, references, options, org);
    }

    // ------------------------------------------------------------ pure build

    public static ErdDocument Build(
        IReadOnlyList<ErdObjectInfo> objects,
        IReadOnlyList<ErdFieldReference> references,
        ErdBuildOptions options,
        string? org,
        string title = "ERD")
    {
        var byName = objects
            .Where(item => !item.IsSystemObject)
            .Where(item => options.IncludeStandardObjects || item.IsCustom)
            .GroupBy(item => item.ApiName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var scope = new HashSet<string>(byName.Keys, StringComparer.OrdinalIgnoreCase);
        var relationships = new List<ErdRelationship>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in references)
        {
            if (!scope.Contains(reference.ObjectApiName))
            {
                continue;
            }

            foreach (var parent in reference.ReferenceTo)
            {
                if (!scope.Contains(parent))
                {
                    continue;
                }

                var key = reference.ObjectApiName + "|" + parent + "|" + reference.FieldApiName;
                if (!seen.Add(key))
                {
                    continue;
                }

                relationships.Add(new ErdRelationship
                {
                    ChildApiName = reference.ObjectApiName,
                    ParentApiName = parent,
                    FieldApiName = reference.FieldApiName,
                    FieldLabel = reference.FieldLabel,
                    IsMasterDetail = reference.IsMasterDetail,
                    IsRequired = reference.IsMasterDetail || !reference.IsNillable,
                });
            }
        }

        var entities = scope
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, options.MaxObjects))
            .Select(name =>
            {
                var info = byName[name];
                var fields = new List<ErdField>
                {
                    new() { ApiName = "Id", Label = "Id", DataType = "Id", IsPrimaryKey = true },
                };

                fields.AddRange(references
                    .Where(reference => string.Equals(reference.ObjectApiName, name, StringComparison.OrdinalIgnoreCase))
                    .Where(reference => relationships.Any(relationship =>
                        string.Equals(relationship.ChildApiName, name, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(relationship.FieldApiName, reference.FieldApiName, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(reference => reference.FieldApiName, StringComparer.OrdinalIgnoreCase)
                    .Select(reference => new ErdField
                    {
                        ApiName = reference.FieldApiName,
                        Label = reference.FieldLabel,
                        DataType = "reference",
                        ReferenceTo = reference.ReferenceTo,
                        IsRequired = reference.IsMasterDetail || !reference.IsNillable,
                        IsMasterDetail = reference.IsMasterDetail,
                    }));

                return new ErdEntity
                {
                    ApiName = info.ApiName,
                    Label = info.Label,
                    IsCustom = info.IsCustom,
                    Fields = fields,
                };
            })
            .ToList();

        var included = new HashSet<string>(entities.Select(entity => entity.ApiName), StringComparer.OrdinalIgnoreCase);
        return new ErdDocument
        {
            Title = title,
            Org = org,
            NameStyle = options.NameStyle,
            Entities = entities,
            Relationships = relationships
                .Where(relationship => included.Contains(relationship.ChildApiName) && included.Contains(relationship.ParentApiName))
                .ToList(),
        };
    }

    // ---------------------------------------------------------------- parsing

    /// <summary>EntityDefinition のレコードを解釈する。</summary>
    public static IReadOnlyList<ErdObjectInfo> ParseObjects(IEnumerable<JsonElement> records)
    {
        var result = new List<ErdObjectInfo>();
        foreach (var record in records)
        {
            var name = GetString(record, "QualifiedApiName");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            result.Add(new ErdObjectInfo(
                name!,
                GetString(record, "Label") ?? name!,
                name!.EndsWith("__c", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("__mdt", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("__e", StringComparison.OrdinalIgnoreCase),
                record.TryGetProperty("IsCustomSetting", out var custom) && custom.ValueKind == JsonValueKind.True));
        }

        return result;
    }

    /// <summary>FieldDefinition のレコード（参照項目）を解釈する。EntityDefinition はネスト / 文字列の両方を受け付ける。</summary>
    public static IReadOnlyList<ErdFieldReference> ParseFieldReferences(IEnumerable<JsonElement> records)
    {
        var result = new List<ErdFieldReference>();
        foreach (var record in records)
        {
            var objectName = ReadEntityName(record);
            var fieldName = GetString(record, "QualifiedApiName");
            if (string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(fieldName))
            {
                continue;
            }

            var targets = new List<string>();
            if (record.TryGetProperty("ReferenceTo", out var referenceTo))
            {
                // Tooling API は { "referenceTo": ["Contact"] } 形式で返す（素の配列も受け付ける）
                var array = referenceTo.ValueKind == JsonValueKind.Array
                    ? referenceTo
                    : referenceTo.ValueKind == JsonValueKind.Object && referenceTo.TryGetProperty("referenceTo", out var inner)
                        ? inner
                        : default;
                if (array.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in array.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } target)
                        {
                            targets.Add(target);
                        }
                    }
                }
            }

            if (targets.Count == 0)
            {
                continue;
            }

            var dataType = GetString(record, "DataType") ?? "Lookup";
            var nillable = !record.TryGetProperty("IsNillable", out var nillableElement) || nillableElement.ValueKind != JsonValueKind.False;
            result.Add(new ErdFieldReference(
                objectName!,
                fieldName!,
                GetString(record, "Label") ?? fieldName!,
                dataType,
                targets,
                nillable));
        }

        return result;
    }

    private static string? ReadEntityName(JsonElement record)
    {
        if (!record.TryGetProperty("EntityDefinition", out var entity))
        {
            return null;
        }

        return entity.ValueKind switch
        {
            JsonValueKind.String => entity.GetString(),
            JsonValueKind.Object => GetString(entity, "QualifiedApiName"),
            _ => null,
        };
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool IsSystemObject(string apiName, bool isCustomSetting)
    {
        if (isCustomSetting)
        {
            return true;
        }

        return SystemSuffixes.Any(suffix => apiName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<List<string>> Chunk(IEnumerable<string> source, int size)
    {
        var buffer = new List<string>(size);
        foreach (var item in source)
        {
            buffer.Add(item);
            if (buffer.Count == size)
            {
                yield return buffer;
                buffer = new List<string>(size);
            }
        }

        if (buffer.Count > 0)
        {
            yield return buffer;
        }
    }

    private async Task<List<JsonElement>> QueryAllAsync(string org, string soql, bool useToolingApi, CancellationToken cancellationToken)
    {
        var records = new List<JsonElement>();
        using var document = await _rest.QueryAsync(org, soql, useToolingApi, cancellationToken).ConfigureAwait(false);
        AddRecords(document.RootElement, records);
        var next = GetString(document.RootElement, "nextRecordsUrl");
        while (!string.IsNullOrEmpty(next) && records.Count < MaxRows)
        {
            using var page = await _rest.GetPageAsync(org, next!, cancellationToken).ConfigureAwait(false);
            AddRecords(page.RootElement, records);
            next = GetString(page.RootElement, "nextRecordsUrl");
        }

        return records;
    }

    private async Task<List<ErdFieldReference>> QueryFieldReferencesAsync(string org, List<string> objectNames, CancellationToken cancellationToken)
    {
        var names = string.Join(",", objectNames.Select(name => "'" + name.Replace("'", "\\'") + "'"));
        // DataType は "Lookup(Account)" / "MasterDetail(Account)" のように型引数付きで返るため、
        // サーバー側で絞り込まず ReferenceTo が入っている行だけをクライアント側で採用する。
        var soql =
            "SELECT EntityDefinition.QualifiedApiName, QualifiedApiName, Label, DataType, ReferenceTo, IsNillable " +
            "FROM FieldDefinition " +
            "WHERE EntityDefinition.QualifiedApiName IN (" + names + ")";

        var records = await QueryAllAsync(org, soql, useToolingApi: true, cancellationToken).ConfigureAwait(false);
        return ParseFieldReferences(records).ToList();
    }

    private static void AddRecords(JsonElement root, List<JsonElement> records)
    {
        if (!root.TryGetProperty("records", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var record in array.EnumerateArray())
        {
            records.Add(record.Clone());
        }
    }
}
