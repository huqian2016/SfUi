using System.Text.Json;

namespace SfUi.Core;

/// <summary>レコード比較の比較対象項目（API 名 + 表示ラベル）。</summary>
public sealed record OrgRecordCompareField(string ApiName, string Label);

/// <summary>レコード比較の条件（オブジェクト・照合キー・比較項目・件数上限）。</summary>
public sealed record OrgRecordCompareRequest(
    string ObjectApiName,
    string KeyField,
    IReadOnlyList<OrgRecordCompareField> Fields,
    int Limit)
{
    /// <summary>件数上限（1〜MaxLimit にクランプ。0 以下は既定値）。</summary>
    public int EffectiveLimit => Math.Clamp(
        Limit <= 0 ? OrgRecordCompareService.DefaultLimit : Limit,
        1,
        OrgRecordCompareService.MaxLimit);
}

/// <summary>1 レコード分の取得結果（Id = レコードページリンク用、Key = 照合キー、Values = 比較項目の生値）。</summary>
public sealed record OrgRecordValue(string Id, string Key, IReadOnlyList<string?> Values);

/// <summary>1 組織 × 1 クエリの結果（State = Value / Failed）。</summary>
public sealed record OrgRecordQueryResult(string OrgKey, OrgCompareCellState State, IReadOnlyList<OrgRecordValue> Records);

/// <summary>1 レコード分の項目別詳細（行 = 比較項目、列 = 組織）。差分行は IsDiff で判定済み。</summary>
public sealed record CompareRecordDetailModel(
    string ObjectApiName,
    string KeyValue,
    IReadOnlyList<OrgCompareOrgColumn> Orgs,
    IReadOnlyList<OrgCompareRow> Rows);

/// <summary>
/// レコード比較（Phase 3）。オブジェクト・照合キー・比較項目を指定して各組織で REST SOQL を実行し、
/// キーで突合した比較表を組み立てる。結果はキャッシュしない（実行のたびに最新を取得。1 ページ目のみ・上限あり）。
/// </summary>
public sealed class OrgRecordCompareService
{
    /// <summary>件数上限の既定値。</summary>
    public const int DefaultLimit = 200;

    /// <summary>件数上限の最大値（REST の 1 ページ上限）。</summary>
    public const int MaxLimit = 2000;

    private readonly SalesforceRestClient _rest;
    private readonly AppLog _log;

    public OrgRecordCompareService(SalesforceRestClient rest, AppLog log)
    {
        _rest = rest;
        _log = log;
    }

    /// <summary>全組織で順にクエリを実行する（1 組織の失敗は Failed として記録し継続）。</summary>
    public async Task<IReadOnlyList<OrgRecordQueryResult>> QueryAllAsync(
        IReadOnlyList<OrgInfo> orgs,
        OrgRecordCompareRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        var results = new List<OrgRecordQueryResult>(orgs.Count);
        foreach (var org in orgs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var orgKey = OrgInfoCacheStore.GetOrgKey(org);
            progress?.Report(UiText.T("Compare_RecordsFetchingFmt", org.DisplayName, request.ObjectApiName));
            try
            {
                results.Add(await QueryOrgAsync(org, request, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warn($"レコード比較: {org.DisplayName} / {request.ObjectApiName} の取得に失敗しました: {ex.Message}");
                results.Add(new OrgRecordQueryResult(orgKey, OrgCompareCellState.Failed, Array.Empty<OrgRecordValue>()));
            }
        }

        return results;
    }

    /// <summary>1 組織分のクエリを実行する（SELECT Id, 照合キー, 比較項目… FROM オブジェクト LIMIT n）。</summary>
    public async Task<OrgRecordQueryResult> QueryOrgAsync(
        OrgInfo org,
        OrgRecordCompareRequest request,
        CancellationToken cancellationToken = default)
    {
        var soql = BuildSoql(request);
        using var document = await _rest
            .QueryAsync(org.Username, soql, useToolingApi: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var records = new List<OrgRecordValue>();
        if (document.RootElement.TryGetProperty("records", out var array))
        {
            foreach (var record in array.EnumerateArray())
            {
                // リンク用に Id は必須（取得できないレコードはスキップ）
                var id = record.TryGetProperty("Id", out var idElement) ? ValueText(idElement) : null;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                var key = record.TryGetProperty(request.KeyField, out var keyElement) ? ValueText(keyElement) : null;
                var values = new string?[request.Fields.Count];
                for (var i = 0; i < request.Fields.Count; i++)
                {
                    values[i] = record.TryGetProperty(request.Fields[i].ApiName, out var value) ? ValueText(value) : null;
                }

                records.Add(new OrgRecordValue(id, string.IsNullOrWhiteSpace(key) ? id : key, values));
            }
        }

        return new OrgRecordQueryResult(OrgInfoCacheStore.GetOrgKey(org), OrgCompareCellState.Value, records);
    }

    /// <summary>SOQL を組み立てる（純関数・テスト対象）。識別子は英字で始まる英数字と _ のみ許可する。</summary>
    public static string BuildSoql(OrgRecordCompareRequest request)
    {
        ValidateIdentifier(request.ObjectApiName, nameof(request.ObjectApiName));
        ValidateIdentifier(request.KeyField, nameof(request.KeyField));

        var columns = new List<string> { "Id", request.KeyField };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Id", request.KeyField };
        foreach (var field in request.Fields)
        {
            ValidateIdentifier(field.ApiName, nameof(request.Fields));
            if (seen.Add(field.ApiName))
            {
                columns.Add(field.ApiName);
            }
        }

        return $"SELECT {string.Join(", ", columns)} FROM {request.ObjectApiName} LIMIT {request.EffectiveLimit}";
    }

    /// <summary>組織ごとのクエリ結果から比較表を組み立てる（純関数・テスト対象）。</summary>
    public static OrgCompareTable BuildTable(
        OrgCompareCategory category,
        IReadOnlyList<OrgCompareOrgColumn> orgs,
        IReadOnlyList<OrgRecordQueryResult> results,
        OrgRecordCompareRequest request)
    {
        var byOrg = new Dictionary<string, Dictionary<string, OrgRecordValue>>(StringComparer.Ordinal);
        var states = new Dictionary<string, OrgCompareCellState>(StringComparer.Ordinal);
        foreach (var org in orgs)
        {
            var result = results.FirstOrDefault(r => string.Equals(r.OrgKey, org.OrgKey, StringComparison.Ordinal));
            states[org.OrgKey] = result?.State ?? OrgCompareCellState.Failed;

            var dict = new Dictionary<string, OrgRecordValue>(StringComparer.OrdinalIgnoreCase);
            if (result is { State: OrgCompareCellState.Value })
            {
                foreach (var record in result.Records)
                {
                    dict.TryAdd(string.IsNullOrWhiteSpace(record.Key) ? record.Id : record.Key, record);
                }
            }

            byOrg[org.OrgKey] = dict;
        }

        var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dict in byOrg.Values)
        {
            foreach (var key in dict.Keys)
            {
                keys.Add(key);
            }
        }

        var rows = new List<OrgCompareRow>(keys.Count);
        foreach (var key in keys)
        {
            var cells = new List<OrgCompareCell>(orgs.Count);
            foreach (var org in orgs)
            {
                if (states[org.OrgKey] == OrgCompareCellState.NotFetched)
                {
                    cells.Add(OrgCompareCells.NotFetched);
                    continue;
                }

                if (states[org.OrgKey] != OrgCompareCellState.Value)
                {
                    cells.Add(OrgCompareCells.Failed);
                    continue;
                }

                if (!byOrg[org.OrgKey].TryGetValue(key, out var record))
                {
                    cells.Add(OrgCompareCells.Missing);
                    continue;
                }

                var text = BuildCellText(request.Fields, record.Values);
                var link = OrgInfoUrlBuilder.RecordOrNull(org.InstanceUrl, request.ObjectApiName, record.Id);
                cells.Add(new OrgCompareCell(OrgCompareCellState.Value, text, link, record.Values.ToArray()));
            }

            rows.Add(new OrgCompareRow(key, key, OrgCompareService.IsRowDiff(cells), cells));
        }

        return new OrgCompareTable(category.Id, orgs, rows);
    }

    /// <summary>
    /// 1 レコード分の項目別詳細（行 = 比較項目、列 = 組織）を組み立てる（純関数・テスト対象）。
    /// 対象レコードが表に無いときは null。空値は「（空）」表示（レコードなしの — と区別する）。
    /// </summary>
    public static CompareRecordDetailModel? BuildDetail(
        string objectApiName,
        string keyValue,
        IReadOnlyList<OrgCompareOrgColumn> orgs,
        IReadOnlyList<OrgRecordCompareField> fields,
        IReadOnlyList<OrgCompareRow> rows)
    {
        var source = rows.FirstOrDefault(r => string.Equals(r.Key, keyValue, StringComparison.OrdinalIgnoreCase));
        if (source is null || fields.Count == 0)
        {
            return null;
        }

        var detailRows = new List<OrgCompareRow>(fields.Count);
        for (var i = 0; i < fields.Count; i++)
        {
            var cells = new List<OrgCompareCell>(source.Cells.Count);
            foreach (var cell in source.Cells)
            {
                if (cell.State != OrgCompareCellState.Value)
                {
                    cells.Add(cell);
                    continue;
                }

                var raw = i < cell.RawValues.Count ? cell.RawValues[i] : null;
                var text = string.IsNullOrEmpty(raw) ? UiText.T("Compare_EmptyValue") : raw;
                cells.Add(new OrgCompareCell(OrgCompareCellState.Value, text, cell.Link, new[] { raw }));
            }

            var label = string.IsNullOrEmpty(fields[i].Label) ? fields[i].ApiName : fields[i].Label;
            detailRows.Add(new OrgCompareRow(fields[i].ApiName, label, OrgCompareService.IsRowDiff(cells), cells));
        }

        return new CompareRecordDetailModel(objectApiName, source.Key, orgs, detailRows);
    }

    /// <summary>セルテキスト（「ラベル: 値」を ・ で連結。空値の項目は省略）。</summary>
    private static string? BuildCellText(IReadOnlyList<OrgRecordCompareField> fields, IReadOnlyList<string?> values)
    {
        var parts = new List<string>(fields.Count);
        for (var i = 0; i < fields.Count && i < values.Count; i++)
        {
            var value = values[i];
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var label = string.IsNullOrEmpty(fields[i].Label) ? fields[i].ApiName : fields[i].Label;
            parts.Add($"{label}: {value}");
        }

        return parts.Count == 0 ? null : string.Join(OrgCompareService.CellSeparator, parts);
    }

    private static string? ValueText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => element.GetRawText(),
    };

    private static void ValidateIdentifier(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || !IsIdentifier(value))
        {
            throw new ArgumentException($"Invalid SOQL identifier: '{value}'", paramName);
        }
    }

    private static bool IsIdentifier(string value)
    {
        if (!char.IsAsciiLetter(value[0]))
        {
            return false;
        }

        return value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }
}
