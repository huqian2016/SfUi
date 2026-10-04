using System.Collections.Concurrent;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// レコードアクセス（UserRecordAccess）を REST で取得する。
/// 制約（実測 2026-10-04）: UserId は単一 ID のみ（IN 不可）/ 1 クエリの結果は 200 行まで /
/// SELECT できるのは RecordId・Has*Access・MaxAccessLevel のみ。
/// </summary>
public sealed class RecordAccessService
{
    /// <summary>UserRecordAccess の 1 クエリ上限（結果 200 行）。</summary>
    public const int MaxRecordsPerQuery = 200;

    /// <summary>対象レコード抽出の既定上限。</summary>
    public const int DefaultMaxRecords = 10_000;

    /// <summary>レコード辞書に格納するオブジェクト API 名（attributes.type）の特殊キー。表示列には含めない。</summary>
    public const string TypeKey = "attributes.type";

    private readonly SalesforceRestClient _rest;
    private readonly AppLog _log;

    public RecordAccessService(SalesforceRestClient rest, AppLog log)
    {
        _rest = rest;
        _log = log;
    }

    /// <summary>有効ユーザーの一覧（名前 / ユーザー名）を取得する。</summary>
    public async Task<IReadOnlyList<RecordAccessUser>> ListActiveUsersAsync(
        string targetOrg,
        CancellationToken cancellationToken = default)
    {
        var result = await RestQueryPager.QueryAllAsync(
            _rest, targetOrg,
            "SELECT Id, Name, Username FROM User WHERE IsActive = true ORDER BY Name",
            5_000, cancellationToken).ConfigureAwait(false);
        return ParseUsers(result.Records);
    }

    /// <summary>対象レコードの抽出 SOQL を実行する（queryMore 対応・上限で打ち切り）。</summary>
    public async Task<RecordQueryResult> QueryRecordsAsync(
        string targetOrg,
        string soql,
        int maxRecords = DefaultMaxRecords,
        CancellationToken cancellationToken = default)
    {
        var result = await RestQueryPager.QueryAllAsync(_rest, targetOrg, soql, maxRecords, cancellationToken).ConfigureAwait(false);
        return BuildRecordQueryResult(result.Records, result.Truncated);
    }

    /// <summary>1 ユーザー分のレコードアクセス権を取得する（recordIds は 200 件ずつ分割して問い合わせる）。</summary>
    public async Task<IReadOnlyDictionary<string, UserRecordAccessFlags>> GetAccessFlagsAsync(
        string targetOrg,
        string userId,
        IReadOnlyList<string> recordIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, UserRecordAccessFlags>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(userId) || recordIds.Count == 0)
        {
            return result;
        }

        foreach (var chunk in ChunkIds(recordIds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var soql = BuildAccessQuery(userId, chunk);
            var rows = await RestQueryPager.QueryAllAsync(
                _rest, targetOrg, soql, MaxRecordsPerQuery, cancellationToken).ConfigureAwait(false);
            foreach (var (recordId, flags) in ParseAccessRows(rows.Records))
            {
                result[recordId] = flags;
            }
        }

        // 行が返らない RecordId（権限なし・対象外）は全 false で補完する。
        foreach (var recordId in recordIds)
        {
            result.TryAdd(recordId, UserRecordAccessFlags.None);
        }

        return result;
    }

    /// <summary>複数ユーザー分のレコードアクセス権を取得する（ユーザー単位で並列・同時実行数は既定 4）。</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, UserRecordAccessFlags>>> GetAccessFlagsForUsersAsync(
        string targetOrg,
        IReadOnlyList<string> userIds,
        IReadOnlyList<string> recordIds,
        int concurrency = 4,
        CancellationToken cancellationToken = default)
    {
        var result = new ConcurrentDictionary<string, IReadOnlyDictionary<string, UserRecordAccessFlags>>(StringComparer.Ordinal);
        if (userIds.Count == 0 || recordIds.Count == 0)
        {
            return result;
        }

        _log.Info($"レコードアクセスを取得: {targetOrg} / ユーザー {userIds.Count} 人 × レコード {recordIds.Count} 件");
        await Parallel.ForEachAsync(
            userIds,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, concurrency),
                CancellationToken = cancellationToken,
            },
            async (userId, token) =>
            {
                result[userId] = await GetAccessFlagsAsync(targetOrg, userId, recordIds, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        return result;
    }

    // ---------- 解析（テスト用に公開） ----------

    /// <summary>User の行を有効ユーザー一覧に変換する。</summary>
    public static IReadOnlyList<RecordAccessUser> ParseUsers(IReadOnlyList<JsonElement> rows)
    {
        var users = new List<RecordAccessUser>(rows.Count);
        foreach (var row in rows)
        {
            var id = GetString(row, "Id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            users.Add(new RecordAccessUser(id, GetString(row, "Name"), GetString(row, "Username")));
        }

        return users;
    }

    /// <summary>SOQL の応答行をレコード一覧（列名 + 値）に変換する。</summary>
    public static RecordQueryResult BuildRecordQueryResult(IReadOnlyList<JsonElement> rows, bool truncated)
    {
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in row.EnumerateObject())
            {
                if (property.NameEquals("attributes"))
                {
                    continue;
                }

                if (seen.Add(property.Name))
                {
                    columns.Add(property.Name);
                }
            }
        }

        var records = new List<IReadOnlyDictionary<string, string?>>(rows.Count);
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var column in columns)
            {
                values[column] = row.TryGetProperty(column, out var value) ? ValueToString(value) : null;
            }

            values[TypeKey] = row.TryGetProperty("attributes", out var attributes) &&
                              attributes.ValueKind == JsonValueKind.Object &&
                              attributes.TryGetProperty("type", out var type) &&
                              type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;

            records.Add(values);
        }

        return new RecordQueryResult(records, columns, truncated);
    }

    /// <summary>UserRecordAccess のクエリ文字列を組み立てる（UserId は単一・RecordId は IN リスト）。</summary>
    public static string BuildAccessQuery(string userId, IReadOnlyList<string> recordIds)
    {
        var ids = string.Join(", ", recordIds.Select(id => $"'{OrgInfoQueryBuilder.EscapeSoqlString(id)}'"));
        return "SELECT RecordId, HasReadAccess, HasEditAccess, HasDeleteAccess, HasTransferAccess " +
               $"FROM UserRecordAccess WHERE UserId = '{OrgInfoQueryBuilder.EscapeSoqlString(userId)}' AND RecordId IN ({ids})";
    }

    /// <summary>UserRecordAccess の行を RecordId → フラグに変換する。</summary>
    public static IReadOnlyList<(string RecordId, UserRecordAccessFlags Flags)> ParseAccessRows(IReadOnlyList<JsonElement> rows)
    {
        var list = new List<(string, UserRecordAccessFlags)>(rows.Count);
        foreach (var row in rows)
        {
            var recordId = GetString(row, "RecordId");
            if (string.IsNullOrEmpty(recordId))
            {
                continue;
            }

            list.Add((recordId, new UserRecordAccessFlags(
                GetBool(row, "HasReadAccess"),
                GetBool(row, "HasEditAccess"),
                GetBool(row, "HasDeleteAccess"),
                GetBool(row, "HasTransferAccess"))));
        }

        return list;
    }

    /// <summary>RecordId を 200 件ずつのチャンクに分割する。</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ChunkIds(
        IReadOnlyList<string> recordIds,
        int size = MaxRecordsPerQuery)
    {
        var chunks = new List<IReadOnlyList<string>>();
        for (var i = 0; i < recordIds.Count; i += size)
        {
            chunks.Add(recordIds.Skip(i).Take(size).ToList());
        }

        return chunks;
    }

    // ---------- 内部 ----------

    private static string? ValueToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => element.GetRawText(),
    };

    private static string GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetBool(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.True;
}
