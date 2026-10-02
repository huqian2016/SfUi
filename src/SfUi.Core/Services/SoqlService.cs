using System.Data;
using System.Diagnostics;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>SOQL 実行結果（DataTable + メタ情報 + 生 JSON）。</summary>
public sealed class SoqlResult
{
    public required DataTable Table { get; init; }

    /// <summary>クエリ全体の件数（Salesforce が返す totalSize）。</summary>
    public int TotalSize { get; init; }

    public bool Done { get; init; }

    /// <summary>履歴保存やコピーに使う records の JSON。</summary>
    public required string RawJson { get; init; }

    public int RowCount => Table.Rows.Count;
}

/// <summary>SOQL 実行結果と使用エンジン。</summary>
public sealed record SoqlExecution(SoqlResult Result, string Engine, TimeSpan Duration);

/// <summary>Salesforce の records JSON を DataTable へ平坦化する。</summary>
public static class SoqlResultFactory
{
    /// <summary>
    /// records（REST / CLI 共通の形状）から DataTable を構築する。
    /// ネストしたオブジェクトは「Account.Name」のようにドット区切りで列にする（attributes は除外）。
    /// 配列（サブクエリ等）は JSON 文字列として 1 セルに格納する。
    /// </summary>
    public static SoqlResult FromJsonRecords(IEnumerable<JsonElement> records, int totalSize, bool done, string rawJson)
    {
        var table = new DataTable();
        var columns = new List<string>();
        var columnSet = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<Dictionary<string, string?>>();

        foreach (var record in records)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            Flatten(record, prefix: null, values);

            foreach (var key in values.Keys)
            {
                if (columnSet.Add(key))
                {
                    columns.Add(key);
                }
            }

            rows.Add(values);
        }

        foreach (var column in columns)
        {
            table.Columns.Add(column, typeof(string));
        }

        foreach (var values in rows)
        {
            var row = table.NewRow();
            foreach (var (key, value) in values)
            {
                row[key] = value is null ? DBNull.Value : value;
            }

            table.Rows.Add(row);
        }

        return new SoqlResult
        {
            Table = table,
            TotalSize = totalSize,
            Done = done,
            RawJson = rawJson,
        };
    }

    internal static void Flatten(JsonElement element, string? prefix, Dictionary<string, string?> values)
    {
        var name = prefix ?? "value";

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("attributes"))
                    {
                        continue;
                    }

                    var childName = prefix is null ? property.Name : $"{prefix}.{property.Name}";
                    Flatten(property.Value, childName, values);
                }

                break;

            case JsonValueKind.Array:
                values[name] = element.GetRawText();
                break;

            case JsonValueKind.String:
                values[name] = element.GetString();
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                values[name] = element.GetRawText();
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                values[name] = null;
                break;

            default:
                values[name] = element.GetRawText();
                break;
        }
    }
}

/// <summary>
/// SOQL 実行サービス。REST API（トークン使用）を優先し、失敗時は sf data query にフォールバックする。
/// </summary>
public sealed class SoqlService
{
    private const int MaxPages = 100;

    /// <summary>明らかな構文エラーの場合に CLI 再実行しても同じ結果になるエラーコード。</summary>
    private static readonly HashSet<string> QuerySyntaxErrorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MALFORMED_QUERY",
        "INVALID_TYPE",
        "INVALID_FIELD",
        "INVALID_QUERY",
        "INVALID_QUERY_LOCATOR",
        "INVALID_FILTER_OPERATOR",
        "INVALID_QUERY_FILTER_OPERATOR",
        "INVALID_WHERE_CLAUSE",
        "INVALID_ORDERBY_CLAUSE",
        "INVALID_GROUPBY_CLAUSE",
    };

    private readonly SalesforceRestClient _rest;
    private readonly SfCliRunner _runner;
    private readonly AppLog _log;

    public SoqlService(SalesforceRestClient rest, SfCliRunner runner, AppLog log)
    {
        _rest = rest;
        _runner = runner;
        _log = log;
    }

    /// <summary>SOQL を実行する（preferRest=true で REST 優先、失敗時 CLI フォールバック）。</summary>
    public async Task<SoqlExecution> ExecuteSoqlAsync(
        string targetOrg,
        string soql,
        bool useToolingApi = false,
        bool preferRest = true,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetOrg))
        {
            throw new ArgumentException("対象組織が指定されていません。", nameof(targetOrg));
        }

        if (string.IsNullOrWhiteSpace(soql))
        {
            throw new ArgumentException("SOQL が入力されていません。", nameof(soql));
        }

        var stopwatch = Stopwatch.StartNew();

        if (preferRest)
        {
            try
            {
                var result = await ExecuteViaRestAsync(targetOrg, soql, useToolingApi, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                return new SoqlExecution(result, useToolingApi ? "REST (Tooling)" : "REST", stopwatch.Elapsed);
            }
            catch (SalesforceApiException ex) when (!IsQuerySyntaxError(ex))
            {
                _log.Warn($"REST 実行に失敗したため CLI で再実行します: {ex.Message}");
            }
        }

        var cliResult = await ExecuteViaCliAsync(targetOrg, soql, useToolingApi, workingDirectory, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        return new SoqlExecution(cliResult, "CLI (sf data query)", stopwatch.Elapsed);
    }

    private static bool IsQuerySyntaxError(SalesforceApiException ex) =>
        ex.ErrorCode is not null && QuerySyntaxErrorCodes.Contains(ex.ErrorCode);

    private async Task<SoqlResult> ExecuteViaRestAsync(string targetOrg, string soql, bool useToolingApi, CancellationToken cancellationToken)
    {
        var records = new List<JsonElement>();
        int totalSize;
        bool done;
        string? nextRecordsUrl;

        using (var document = await _rest.QueryAsync(targetOrg, soql, useToolingApi, cancellationToken).ConfigureAwait(false))
        {
            var root = document.RootElement;
            totalSize = GetInt(root, "totalSize");
            done = GetBool(root, "done");
            AddRecords(root, records);
            nextRecordsUrl = GetString(root, "nextRecordsUrl");
        }

        var pages = 0;
        while (!done && !string.IsNullOrEmpty(nextRecordsUrl) && pages++ < MaxPages)
        {
            using var page = await _rest.GetPageAsync(targetOrg, nextRecordsUrl!, cancellationToken).ConfigureAwait(false);
            var root = page.RootElement;
            AddRecords(root, records);
            done = GetBool(root, "done");
            nextRecordsUrl = GetString(root, "nextRecordsUrl");
        }

        totalSize = Math.Max(totalSize, records.Count);
        var rawJson = JsonSerializer.Serialize(records);
        return SoqlResultFactory.FromJsonRecords(records, totalSize, done, rawJson);
    }

    private async Task<SoqlResult> ExecuteViaCliAsync(string targetOrg, string soql, bool useToolingApi, string? workingDirectory, CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "data", "query",
            "--target-org", targetOrg,
            "--query", soql,
            "--json",
        };
        if (useToolingApi)
        {
            arguments.Add("--use-tooling-api");
        }

        var raw = await _runner.RunAsync(arguments, workingDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        var command = SfCommandResult.From(raw);
        if (!command.IsSuccess || command.Result is not { } result || result.ValueKind != JsonValueKind.Object)
        {
            throw new SfCliException($"SOQL 実行に失敗しました: {command.ErrorMessage}", raw);
        }

        var records = new List<JsonElement>();
        AddRecords(result, records);
        var totalSize = GetInt(result, "totalSize");
        var done = GetBool(result, "done");
        var rawJson = JsonSerializer.Serialize(records);
        return SoqlResultFactory.FromJsonRecords(records, totalSize, done, rawJson);
    }

    private static void AddRecords(JsonElement root, List<JsonElement> records)
    {
        if (root.TryGetProperty("records", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in array.EnumerateArray())
            {
                records.Add(record.Clone());
            }
        }
    }

    private static int GetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static bool GetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
