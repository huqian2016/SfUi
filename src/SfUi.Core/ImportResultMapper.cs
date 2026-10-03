using System.Globalization;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>API 応答 → 行別結果への変換。</summary>
public static class ImportResultMapper
{
    /// <summary>composite/sobjects の応答（配列）を行別結果へ変換する（リクエスト順と対応）。</summary>
    public static List<ImportRowResult> FromCompositeResponse(string json, IReadOnlyList<ImportPlannedRow> batch)
    {
        var results = new List<ImportRowResult>(batch.Count);

        using var document = JsonDocument.Parse(json);
        var index = 0;

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var row = index < batch.Count ? batch[index] : null;
            var success = element.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            var id = success && element.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            var error = success ? null : DescribeErrors(element);
            results.Add(new ImportRowResult(row?.RowIndex ?? -1, success, id, error));
            index++;
        }

        // 応答が不足している場合は残りを失敗として補完する
        while (index < batch.Count)
        {
            results.Add(new ImportRowResult(batch[index].RowIndex, false, null, UiText.T("DataIo_Err_NoResponse")));
            index++;
        }

        return results;
    }

    /// <summary>1 レコード応答（Upsert の PATCH など）を結果へ変換する。</summary>
    public static ImportRowResult FromSingleResponse(string json, int rowIndex)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            root = root[0];
        }

        var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        var id = root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
        var error = success ? null : DescribeErrors(root);
        return new ImportRowResult(rowIndex, success, id, error);
    }

    /// <summary>errors 配列（composite / 単一レコード共通）を 1 つのメッセージにまとめる。</summary>
    public static string? DescribeErrors(JsonElement element)
    {
        if (!element.TryGetProperty("errors", out var errors) ||
            errors.ValueKind != JsonValueKind.Array ||
            errors.GetArrayLength() == 0)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var error in errors.EnumerateArray())
        {
            var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            var status = error.TryGetProperty("statusCode", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var fields = error.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array
                ? string.Join(", ", f.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()))
                : null;

            var text = !string.IsNullOrEmpty(fields) ? $"{message} [{fields}]" : message;
            if (!string.IsNullOrEmpty(status))
            {
                text = $"{text} ({status})";
            }

            if (!string.IsNullOrEmpty(text))
            {
                parts.Add(text);
            }
        }

        return parts.Count > 0 ? string.Join(" / ", parts) : null;
    }

    /// <summary>Bulk ジョブ情報（result.jobInfo 等）から id・処理数・失敗数を取り出す。</summary>
    public static (string? Id, int Processed, int Failed) ParseBulkJobInfo(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var result))
            {
                root = result;
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("jobInfo", out var jobInfo))
            {
                root = jobInfo;
            }

            return (GetString(root, "id"), GetInt(root, "numberRecordsProcessed") ?? 0, GetInt(root, "numberRecordsFailed") ?? 0);
        }
        catch (JsonException)
        {
            return (null, 0, 0);
        }
    }

    /// <summary>Bulk 結果 JSON からレコード配列を取り出す（result.records / records / ルート配列に対応）。</summary>
    public static List<JsonElement> ExtractRecords(string json)
    {
        var records = new List<JsonElement>();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var result))
            {
                root = result;
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var inner))
            {
                root = inner;
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    records.Add(element.Clone());
                }
            }
        }
        catch (JsonException)
        {
            // 解析できない場合は空
        }

        return records;
    }

    /// <summary>Bulk 失敗結果（sf__Error 付き）を送信計画の行へ対応付ける（値の一致でベストエフォート）。</summary>
    public static List<ImportRowResult> MapBulkFailures(
        IReadOnlyList<JsonElement> resultRecords,
        ImportPlan plan,
        IReadOnlyList<string> originalColumns)
    {
        var lookup = new Dictionary<string, Queue<ImportPlannedRow>>(StringComparer.Ordinal);
        foreach (var row in plan.Rows.Where(r => r.Error is null))
        {
            var key = BuildKey(row, originalColumns);
            if (!lookup.TryGetValue(key, out var queue))
            {
                queue = new Queue<ImportPlannedRow>();
                lookup[key] = queue;
            }

            queue.Enqueue(row);
        }

        var failed = new List<ImportRowResult>();
        foreach (var record in resultRecords)
        {
            var error = GetString(record, "sf__Error");
            if (string.IsNullOrEmpty(error))
            {
                continue;
            }

            var values = originalColumns.Select(c => NormalizeCell(GetString(record, c))).ToList();
            var key = string.Join('\u0001', values);
            ImportPlannedRow? matched = lookup.TryGetValue(key, out var queue) && queue.Count > 0 ? queue.Dequeue() : null;
            failed.Add(new ImportRowResult(matched?.RowIndex ?? -1, false, GetString(record, "sf__Id"), error));
        }

        return failed;
    }

    private static string BuildKey(ImportPlannedRow row, IReadOnlyList<string> columns)
    {
        var values = new List<string>(columns.Count);
        foreach (var column in columns)
        {
            if (string.Equals(column, "Id", StringComparison.OrdinalIgnoreCase))
            {
                values.Add(NormalizeCell(row.Id));
                continue;
            }

            row.Fields.TryGetValue(column, out var value);
            values.Add(NormalizeCell(ImportBatchPlanner.FormatCell(value)));
        }

        return string.Join('\u0001', values);
    }

    /// <summary>値の表記ゆれ（空白・数値の 1.0 と 1 など）を正規化する。</summary>
    private static string NormalizeCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            // 1.0 と 1.000 のような表記ゆれを揃える（末尾 0 を除去）
            var text = number.ToString(CultureInfo.InvariantCulture);
            if (text.Contains('.'))
            {
                text = text.TrimEnd('0').TrimEnd('.');
            }

            return text;
        }

        return trimmed;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
