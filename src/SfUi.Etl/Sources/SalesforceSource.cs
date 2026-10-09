using System.Text.Json;
using System.Text.RegularExpressions;
using SfUi.Core;

namespace SfUi.Etl.Sources;

/// <summary>
/// Salesforce 入力ソース（SOQL）。REST クエリを nextRecordsUrl でページングしながら全件読み込み、
/// 各レコードをフラット化する（標準の "attributes" プロパティは除外。値は文字列 / 数値原文 /
/// true / false / null、ネストした値（リレーション等）は生 JSON テキスト）。
/// </summary>
public sealed class SalesforceSource : IEtlSource
{
    private const int DefaultMaxPages = 500;

    private readonly List<Dictionary<string, object?>> _records = new();
    private readonly List<string> _columns = new();

    /// <summary>組織の SOQL を実行して全ページを読み込む（コンストラクターで同期ロード）。</summary>
    public SalesforceSource(SalesforceRestClient client, string targetOrg, string soql)
        : this(soql, new ClientPages(client, targetOrg)) { }

    /// <summary>テスト用: 任意のページ取得実装で実行する。</summary>
    internal SalesforceSource(string soql, ISoqlPages pages, int maxPages = DefaultMaxPages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(soql);
        ArgumentNullException.ThrowIfNull(pages);
        Soql = soql.Trim();
        Name = ParseObjectName(Soql) ?? "SOQL";

        // 同期コンストラクターから安全に待つ（UI スレッドから呼ばれてもデッドロックしない）
        Task.Run(() => LoadAllAsync(pages, Math.Max(1, maxPages))).GetAwaiter().GetResult();
    }

    /// <summary>SOQL クエリ。</summary>
    public string Soql { get; }

    public string Name { get; }

    public IReadOnlyList<string> Columns => _columns;

    public IEnumerable<object?[]> ReadRows()
    {
        foreach (var record in _records)
        {
            var row = new object?[_columns.Count];
            for (var i = 0; i < _columns.Count; i++)
            {
                record.TryGetValue(_columns[i], out row[i]);
            }

            yield return row;
        }
    }

    private async Task LoadAllAsync(ISoqlPages pages, int maxPages)
    {
        var pageCount = 0;
        string? nextRecordsUrl = null;
        var first = true;

        while (true)
        {
            pageCount++;
            if (pageCount > maxPages)
            {
                throw new InvalidOperationException($"SOQL 取得が最大ページ数 ({maxPages}) を超えました。");
            }

            using var document = first
                ? await pages.QueryAsync(Soql, CancellationToken.None).ConfigureAwait(false)
                : await pages.GetPageAsync(nextRecordsUrl!, CancellationToken.None).ConfigureAwait(false);
            first = false;

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("records", out var records) ||
                records.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("SOQL 応答に records がありません。");
            }

            foreach (var item in records.EnumerateArray())
            {
                AddRecord(item);
            }

            var done = root.TryGetProperty("done", out var doneElement) && doneElement.ValueKind == JsonValueKind.True;
            nextRecordsUrl = root.TryGetProperty("nextRecordsUrl", out var nextElement) && nextElement.ValueKind == JsonValueKind.String
                ? nextElement.GetString()
                : null;

            if (done || string.IsNullOrEmpty(nextRecordsUrl))
            {
                break;
            }
        }
    }

    private void AddRecord(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var record = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
        {
            if (property.NameEquals("attributes"))
            {
                continue;
            }

            if (!_columns.Contains(property.Name, StringComparer.Ordinal))
            {
                _columns.Add(property.Name);
            }

            record[property.Name] = JsonRecords.ToValue(property.Value);
        }

        _records.Add(record);
    }

    private static readonly Regex FromPattern = new(
        @"\bFROM\s+([A-Za-z][A-Za-z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>SOQL から対象オブジェクト名を推定する（見つからなければ null）。</summary>
    internal static string? ParseObjectName(string soql)
    {
        var match = FromPattern.Match(soql);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>SalesforceRestClient をページ取得インターフェイスに適合させる。</summary>
    private sealed class ClientPages : ISoqlPages
    {
        private readonly SalesforceRestClient _client;
        private readonly string _targetOrg;

        public ClientPages(SalesforceRestClient client, string targetOrg)
        {
            _client = client;
            _targetOrg = targetOrg;
        }

        public Task<JsonDocument> QueryAsync(string soql, CancellationToken cancellationToken)
            => _client.QueryAsync(_targetOrg, soql, useToolingApi: false, cancellationToken);

        public Task<JsonDocument> GetPageAsync(string nextRecordsUrl, CancellationToken cancellationToken)
            => _client.GetPageAsync(_targetOrg, nextRecordsUrl, cancellationToken);
    }
}

/// <summary>SOQL ページ取得の抽象（テスト用）。</summary>
internal interface ISoqlPages
{
    Task<JsonDocument> QueryAsync(string soql, CancellationToken cancellationToken);

    Task<JsonDocument> GetPageAsync(string nextRecordsUrl, CancellationToken cancellationToken);
}
