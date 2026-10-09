using System.Net.Http;
using System.Text.Json;
using SfUi.Core;
using SfUi.Etl.Engine;
using SfUi.Etl.Staging;

namespace SfUi.Etl.Targets;

/// <summary>Salesforce 出力ターゲットの設定。</summary>
public sealed class SalesforceTargetOptions
{
    /// <summary>出力先オブジェクト API 名。</summary>
    public required string ObjectName { get; init; }

    /// <summary>出力項目名（<see cref="QueueRow.Values"/> の並びと一致させる）。</summary>
    public required IReadOnlyList<string> Fields { get; init; }

    /// <summary>操作（<see cref="RowOp"/> の insert / update / upsert / delete）。</summary>
    public string Op { get; init; } = RowOp.Insert;

    /// <summary>update / upsert / delete のマッチ キー項目（例: <c>Id</c> や外部 Id 項目）。</summary>
    public string? MatchKeyField { get; init; }

    /// <summary>1 バッチの最大件数（composite の上限は 200）。</summary>
    public int MaxBatchSize { get; init; } = 200;
}

/// <summary>
/// Salesforce REST 出力ターゲット（composite/sobjects の 200 件一括 + マッチ キー解決 + journal 記録）。
/// <list type="bullet">
/// <item>insert: POST composite/sobjects（journal: insert / 成功 Id を <c>_target_id</c> へ）</item>
/// <item>update / upsert: マッチ キーで SOQL 解決 → before-image を journal 記録 → PATCH / POST</item>
/// <item>delete: マッチ キーで解決 → DELETE composite（journal: delete + before-image で巻き戻し可能）</item>
/// <item>巻き戻し（<see cref="IEtlRevertable"/>）: insert → 削除 / update → before-image へ復元 / delete → 再作成</item>
/// </list>
/// </summary>
public sealed class SalesforceTarget : IEtlTarget, IEtlRevertable
{
    private readonly SalesforceRestClient _client;
    private readonly AppLog _log;
    private readonly string _targetOrg;
    private readonly SalesforceTargetOptions _options;
    private readonly int _matchKeyIndex;
    private string _apiVersion = SalesforceRestClient.DefaultApiVersion;
    private Dictionary<string, string> _fieldTypes = new(StringComparer.OrdinalIgnoreCase);

    public SalesforceTarget(SalesforceRestClient client, string targetOrg, SalesforceTargetOptions options, AppLog log)
    {
        _client = client;
        _targetOrg = targetOrg;
        _options = options;
        _log = log;

        _matchKeyIndex = -1;
        if (options.Op is RowOp.Update or RowOp.Upsert or RowOp.Delete)
        {
            if (string.IsNullOrWhiteSpace(options.MatchKeyField))
            {
                throw new ArgumentException("update / upsert / delete には MatchKeyField が必要です。", nameof(options));
            }

            for (var i = 0; i < options.Fields.Count; i++)
            {
                if (string.Equals(options.Fields[i], options.MatchKeyField, StringComparison.OrdinalIgnoreCase))
                {
                    _matchKeyIndex = i;
                    break;
                }
            }

            if (_matchKeyIndex < 0)
            {
                throw new ArgumentException($"マッチ キー '{options.MatchKeyField}' が出力項目に含まれていません。", nameof(options));
            }
        }
    }

    public string Name => "Salesforce:" + _options.ObjectName;

    public int MaxBatchSize => _options.MaxBatchSize;

    /// <summary>describe を取得して接続と項目の存在を確認する（項目型マップもここで確定）。</summary>
    public async Task<bool> TestAsync(CancellationToken ct)
    {
        try
        {
            _apiVersion = await _client.GetApiVersionAsync(_targetOrg, ct).ConfigureAwait(false);
            using var describe = await _client.DescribeAsync(_targetOrg, _options.ObjectName, ct).ConfigureAwait(false);
            _fieldTypes = SalesforcePayloads.ParseFieldTypes(describe.RootElement);

            var unknown = _options.Fields.Where(f => !_fieldTypes.ContainsKey(f)).ToList();
            if (unknown.Count > 0)
            {
                _log.Warn($"{_options.ObjectName} に存在しない項目があります: {string.Join(", ", unknown)}");
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warn($"{_options.ObjectName} の事前チェックに失敗しました: {ex.Message}");
            return false;
        }
    }

    public Task<EtlBatchResult> ApplyBatchAsync(EtlApplyContext context, IReadOnlyList<QueueRow> rows, CancellationToken ct)
        => _options.Op switch
        {
            RowOp.Insert => ApplyInsertAsync(context, rows, ct),
            RowOp.Update or RowOp.Upsert => ApplyUpsertAsync(context, rows, ct),
            RowOp.Delete => ApplyDeleteAsync(context, rows, ct),
            _ => throw new InvalidOperationException($"未対応の操作です: {_options.Op}"),
        };

    private async Task<EtlBatchResult> ApplyInsertAsync(EtlApplyContext context, IReadOnlyList<QueueRow> rows, CancellationToken ct)
    {
        var body = SalesforcePayloads.BuildInsertBody(_options.ObjectName, _options.Fields, rows, _fieldTypes);
        var path = $"/services/data/v{_apiVersion}/composite/sobjects";
        var response = await _client.SendRawAsync(_targetOrg, HttpMethod.Post, path, body, ct).ConfigureAwait(false);
        var parsed = SalesforcePayloads.ParseCompositeResponse(response);
        return new EtlBatchResult(MapInsertResults(context, rows, parsed));
    }

    private async Task<EtlBatchResult> ApplyUpsertAsync(EtlApplyContext context, IReadOnlyList<QueueRow> rows, CancellationToken ct)
    {
        var existing = await ResolveExistingAsync(rows, ct).ConfigureAwait(false);

        var updates = new List<(QueueRow Row, string Id, string BeforeJson)>();
        var inserts = new List<QueueRow>();
        var results = new List<RowApplyResult>();

        foreach (var row in rows)
        {
            var key = KeyOf(row);
            if (existing.TryGetValue(key, out var found))
            {
                updates.Add((row, found.Id, found.BeforeJson));
            }
            else if (_options.Op == RowOp.Upsert)
            {
                inserts.Add(row);
            }
            else
            {
                results.Add(new RowApplyResult(row.RowId, false, Error: $"一致するレコードが見つかりません（{_options.MatchKeyField} = {key}）"));
            }
        }

        if (updates.Count > 0)
        {
            var body = SalesforcePayloads.BuildUpdateBody(
                _options.ObjectName,
                _options.Fields,
                updates.Select(u => (u.Row, u.Id)).ToList(),
                _fieldTypes);
            var path = $"/services/data/v{_apiVersion}/composite/sobjects";
            var response = await _client.SendRawAsync(_targetOrg, HttpMethod.Patch, path, body, ct).ConfigureAwait(false);
            var parsed = SalesforcePayloads.ParseCompositeResponse(response);

            for (var i = 0; i < updates.Count; i++)
            {
                var (row, id, beforeJson) = updates[i];
                var record = i < parsed.Count ? parsed[i] : new CompositeRecordResult(null, false, null, "結果が返されませんでした。");
                if (record.Success)
                {
                    var journalId = context.Store.AddJournal(new JournalEntry(
                        context.StepId, _options.ObjectName, RowOp.Update, id, SourceKey: KeyOf(row), beforeJson, AfterJson(row)));
                    results.Add(new RowApplyResult(row.RowId, true, id, null, false, false, journalId));
                }
                else
                {
                    results.Add(new RowApplyResult(row.RowId, false, null, record.Message, SalesforcePayloads.IsTransientStatus(record.StatusCode)));
                }
            }
        }

        if (inserts.Count > 0)
        {
            var body = SalesforcePayloads.BuildInsertBody(_options.ObjectName, _options.Fields, inserts, _fieldTypes);
            var path = $"/services/data/v{_apiVersion}/composite/sobjects";
            var response = await _client.SendRawAsync(_targetOrg, HttpMethod.Post, path, body, ct).ConfigureAwait(false);
            var parsed = SalesforcePayloads.ParseCompositeResponse(response);
            results.AddRange(MapInsertResults(context, inserts, parsed));
        }

        return new EtlBatchResult(results);
    }

    private async Task<EtlBatchResult> ApplyDeleteAsync(EtlApplyContext context, IReadOnlyList<QueueRow> rows, CancellationToken ct)
    {
        var existing = await ResolveExistingAsync(rows, ct).ConfigureAwait(false);

        var targets = new List<(QueueRow Row, string Id, string BeforeJson)>();
        var results = new List<RowApplyResult>();

        foreach (var row in rows)
        {
            var key = KeyOf(row);
            if (existing.TryGetValue(key, out var found))
            {
                targets.Add((row, found.Id, found.BeforeJson));
            }
            else
            {
                results.Add(new RowApplyResult(row.RowId, false, Skipped: true, Error: $"削除対象が見つかりません（{_options.MatchKeyField} = {key}）"));
            }
        }

        if (targets.Count > 0)
        {
            var path = SalesforcePayloads.BuildDeletePath(_apiVersion, targets.Select(t => t.Id).ToList());
            var response = await _client.SendRawAsync(_targetOrg, HttpMethod.Delete, path, null, ct).ConfigureAwait(false);
            var parsed = SalesforcePayloads.ParseCompositeResponse(response);

            for (var i = 0; i < targets.Count; i++)
            {
                var (row, id, beforeJson) = targets[i];
                var record = i < parsed.Count ? parsed[i] : new CompositeRecordResult(null, false, null, "結果が返されませんでした。");
                if (record.Success)
                {
                    var journalId = context.Store.AddJournal(new JournalEntry(
                        context.StepId, _options.ObjectName, RowOp.Delete, id, SourceKey: KeyOf(row), beforeJson, AfterJson: null));
                    results.Add(new RowApplyResult(row.RowId, true, id, null, false, false, journalId));
                }
                else
                {
                    results.Add(new RowApplyResult(row.RowId, false, null, record.Message, SalesforcePayloads.IsTransientStatus(record.StatusCode)));
                }
            }
        }

        return new EtlBatchResult(results);
    }

    private List<RowApplyResult> MapInsertResults(
        EtlApplyContext context,
        IReadOnlyList<QueueRow> rows,
        IReadOnlyList<CompositeRecordResult> parsed)
    {
        var results = new List<RowApplyResult>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var record = i < parsed.Count ? parsed[i] : new CompositeRecordResult(null, false, null, "結果が返されませんでした。");
            if (record.Success)
            {
                var journalId = context.Store.AddJournal(new JournalEntry(
                    context.StepId, _options.ObjectName, RowOp.Insert, record.Id, SourceKey: null, BeforeJson: null, AfterJson: AfterJson(row)));
                results.Add(new RowApplyResult(row.RowId, true, record.Id, null, false, false, journalId));
            }
            else
            {
                results.Add(new RowApplyResult(row.RowId, false, null, record.Message, SalesforcePayloads.IsTransientStatus(record.StatusCode)));
            }
        }

        return results;
    }

    /// <summary>
    /// journal エントリを巻き戻す（insert → 削除 / update → before-image へ復元 / delete → 再作成）。
    /// 結果の <see cref="RowApplyResult.RowId"/> は journal エントリの Id。
    /// </summary>
    public async Task<EtlBatchResult> RevertBatchAsync(EtlApplyContext context, IReadOnlyList<JournalRow> entries, CancellationToken ct)
    {
        var results = new List<RowApplyResult>(entries.Count);

        // insert の巻き戻し = 作成したレコードを削除
        var insertDeletes = entries
            .Where(e => e.Op == RowOp.Insert && !string.IsNullOrEmpty(e.TargetId))
            .ToList();
        if (insertDeletes.Count > 0)
        {
            var path = SalesforcePayloads.BuildDeletePath(_apiVersion, insertDeletes.Select(e => e.TargetId!).ToList());
            var response = await _client.SendRawAsync(_targetOrg, HttpMethod.Delete, path, null, ct).ConfigureAwait(false);
            MapRevertResults(results, insertDeletes, SalesforcePayloads.ParseCompositeResponse(response), "挿入の取り消し");
        }

        // update の巻き戻し = before-image へ復元（PATCH）
        var updateReverts = entries
            .Where(e => e.Op == RowOp.Update && !string.IsNullOrEmpty(e.TargetId) && !string.IsNullOrEmpty(e.BeforeJson))
            .ToList();
        if (updateReverts.Count > 0)
        {
            var body = SalesforcePayloads.BuildRevertUpdateBody(
                _options.ObjectName,
                updateReverts.Select(e => (e.TargetId!, e.BeforeJson!)).ToList());
            var path = $"/services/data/v{_apiVersion}/composite/sobjects";
            var response = await _client.SendRawAsync(_targetOrg, HttpMethod.Patch, path, body, ct).ConfigureAwait(false);
            MapRevertResults(results, updateReverts, SalesforcePayloads.ParseCompositeResponse(response), "更新の巻き戻し");
        }

        // delete の巻き戻し = before-image から再作成（POST）
        var deleteReverts = entries
            .Where(e => e.Op == RowOp.Delete && !string.IsNullOrEmpty(e.BeforeJson))
            .ToList();
        if (deleteReverts.Count > 0)
        {
            var body = SalesforcePayloads.BuildRevertRecreateBody(
                _options.ObjectName,
                deleteReverts.Select(e => e.BeforeJson!).ToList());
            var path = $"/services/data/v{_apiVersion}/composite/sobjects";
            var response = await _client.SendRawAsync(_targetOrg, HttpMethod.Post, path, body, ct).ConfigureAwait(false);
            MapRevertResults(results, deleteReverts, SalesforcePayloads.ParseCompositeResponse(response), "削除の復元");
        }

        // 対象外（journal 情報不足）
        foreach (var entry in entries)
        {
            if (results.Any(r => r.RowId == entry.Id))
            {
                continue;
            }

            results.Add(new RowApplyResult(entry.Id, false, Error: "journal の情報不足により巻き戻しできません（before-image / targetId なし）。"));
        }

        return new EtlBatchResult(results);
    }

    private static void MapRevertResults(
        List<RowApplyResult> results,
        IReadOnlyList<JournalRow> entries,
        IReadOnlyList<CompositeRecordResult> parsed,
        string label)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var record = i < parsed.Count ? parsed[i] : new CompositeRecordResult(null, false, null, $"{label}: 結果が返されませんでした。");
            results.Add(record.Success
                ? new RowApplyResult(entry.Id, true, record.Id)
                : new RowApplyResult(
                    entry.Id,
                    false,
                    Error: record.Message ?? label + " に失敗しました。",
                    Transient: SalesforcePayloads.IsTransientStatus(record.StatusCode)));
        }
    }

    /// <summary>マッチ キーで既存レコードを解決する（キー値 → Id + before-image JSON）。</summary>
    private async Task<Dictionary<string, (string Id, string BeforeJson)>> ResolveExistingAsync(
        IReadOnlyList<QueueRow> rows,
        CancellationToken ct)
    {
        var map = new Dictionary<string, (string Id, string BeforeJson)>(StringComparer.Ordinal);
        if (rows.Count == 0 || _matchKeyIndex < 0)
        {
            return map;
        }

        var keys = rows.Select(KeyOf).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0)
        {
            return map;
        }

        var soql = SalesforcePayloads.BuildResolveSoql(_options.ObjectName, _options.MatchKeyField!, _options.Fields, keys);
        using var document = await _client.QueryAsync(_targetOrg, soql, cancellationToken: ct).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
        {
            return map;
        }

        foreach (var record in records.EnumerateArray())
        {
            var id = SalesforcePayloads.TryGetProperty(record, "Id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                ? idProp.GetString()
                : null;
            var key = SalesforcePayloads.TryGetProperty(record, _options.MatchKeyField!, out var keyProp)
                ? Convert.ToString(JsonToValue(keyProp), System.Globalization.CultureInfo.InvariantCulture)
                : null;

            if (!string.IsNullOrEmpty(id) && key is not null)
            {
                map[key.Trim()] = (id, SalesforcePayloads.RecordToJson(record, _options.Fields));
            }
        }

        return map;
    }

    private static object? JsonToValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    private string KeyOf(QueueRow row)
        => _matchKeyIndex >= 0 && _matchKeyIndex < row.Values.Length
            ? (Convert.ToString(row.Values[_matchKeyIndex], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Trim()
            : string.Empty;

    private string AfterJson(QueueRow row)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < _options.Fields.Count && i < row.Values.Length; i++)
        {
            values[_options.Fields[i]] = row.Values[i];
        }

        return JsonSerializer.Serialize(values);
    }
}
