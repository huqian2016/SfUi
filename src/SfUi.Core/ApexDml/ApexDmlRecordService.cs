using System.Text.Json;

namespace SfUi.Core;

/// <summary>参照項目 / Id の選択候補として使うレコード 1 件。</summary>
public sealed record ApexDmlRecord(string Id, string? Name)
{
    public string Display => string.IsNullOrWhiteSpace(Name) ? Id : $"{Name} ({Id})";

    public override string ToString() => Display;
}

/// <summary>候補レコードを引く SOQL の組み立てと応答の解析（純関数）。</summary>
public static class ApexDmlRecordQuery
{
    public const int DefaultLimit = 30;

    /// <summary>候補レコード取得の SOQL。<paramref name="hasNameField"/> が false のときは Id のみ。</summary>
    public static string Build(string sobject, string? search, int limit = DefaultLimit, bool hasNameField = true)
    {
        var fields = hasNameField ? "Id, Name" : "Id";
        var soql = $"SELECT {fields} FROM {sobject}";
        var term = search?.Trim() ?? string.Empty;
        if (term.Length > 0)
        {
            var escaped = term.Replace("\\", "\\\\").Replace("'", "\\'");
            soql += hasNameField
                ? $" WHERE Name LIKE '%{escaped}%'"
                : $" WHERE Id LIKE '{escaped}%'";
        }

        soql += hasNameField ? " ORDER BY Name" : " ORDER BY Id";
        return $"{soql} LIMIT {Math.Clamp(limit, 1, 200)}";
    }

    /// <summary>REST のクエリ応答から候補を取り出す（失敗時は空）。</summary>
    public static IReadOnlyList<ApexDmlRecord> Parse(JsonElement root, bool hasNameField = true)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("records", out var records)
            || records.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ApexDmlRecord>();
        }

        var list = new List<ApexDmlRecord>();
        foreach (var record in records.EnumerateArray())
        {
            if (record.ValueKind != JsonValueKind.Object
                || !record.TryGetProperty("Id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = idElement.GetString();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            string? name = null;
            if (hasNameField && record.TryGetProperty("Name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                name = nameElement.GetString();
            }

            list.Add(new ApexDmlRecord(id, name));
        }

        return list;
    }
}

/// <summary>DML 生成ダイアログで参照先レコードの候補を取得する（テスト差し替え用）。</summary>
public interface IApexDmlRecordService
{
    Task<IReadOnlyList<ApexDmlRecord>> SearchAsync(
        string targetOrg,
        string sobject,
        string? search,
        CancellationToken cancellationToken = default);
}

/// <summary>REST クエリで候補レコードを取得する。</summary>
public sealed class ApexDmlRecordService : IApexDmlRecordService
{
    private readonly SalesforceRestClient _rest;
    private readonly SObjectDescribeService _describes;
    private readonly AppLog _log;

    public ApexDmlRecordService(SalesforceRestClient rest, SObjectDescribeService describes, AppLog log)
    {
        _rest = rest;
        _describes = describes;
        _log = log;
    }

    public async Task<IReadOnlyList<ApexDmlRecord>> SearchAsync(
        string targetOrg,
        string sobject,
        string? search,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetOrg) || string.IsNullOrWhiteSpace(sobject))
        {
            return Array.Empty<ApexDmlRecord>();
        }

        var hasName = true;
        try
        {
            var describe = await _describes.DescribeAsync(targetOrg, sobject, false, cancellationToken).ConfigureAwait(false);
            hasName = describe.Fields.Any(f => string.Equals(f.Name, "Name", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _log.Warn($"DML 候補: describe を取得できませんでした（{sobject}）: {ex.Message}");
        }

        var soql = ApexDmlRecordQuery.Build(sobject, search, ApexDmlRecordQuery.DefaultLimit, hasName);
        try
        {
            using var document = await _rest.QueryAsync(targetOrg, soql, false, cancellationToken).ConfigureAwait(false);
            return ApexDmlRecordQuery.Parse(document.RootElement, hasName);
        }
        catch (Exception ex)
        {
            _log.Warn($"DML 候補: レコードを取得できませんでした（{sobject}）: {ex.Message}");
            return Array.Empty<ApexDmlRecord>();
        }
    }
}
