using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>CSV 行 → 送信計画（REST バッチ / Bulk CSV）への変換。</summary>
public static class ImportBatchPlanner
{
    /// <summary>SObject Collections / composite API の 1 バッチ上限。</summary>
    public const int MaxBatchSize = 200;

    /// <summary>CSV 行とマッピングから送信計画を作る（変換エラー・Id 欠落は行に記録し送信対象から除外）。</summary>
    public static ImportPlan BuildPlan(
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<ImportColumnMapping> mappings,
        DataIoObjectDescribe describe,
        DataImportOperation operation,
        string? externalIdField,
        bool emptyAsNull)
    {
        var mapped = mappings.Where(m => m.IsMapped).ToList();
        var planned = new List<ImportPlannedRow>(rows.Count);

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            string? id = null;
            string? externalIdValue = null;
            string? error = null;

            foreach (var mapping in mapped)
            {
                var fieldName = mapping.FieldName!;
                var raw = mapping.ColumnIndex < row.Count ? row[mapping.ColumnIndex] : null;

                // Id は送信フィールドではなく Id プロパティ（Update / Delete / 参考表示）
                if (string.Equals(fieldName, ImportFieldMatcher.IdField, StringComparison.OrdinalIgnoreCase))
                {
                    id = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
                    continue;
                }

                var field = describe.Fields.FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                    ?? new DataIoField(fieldName, fieldName, "string", true, true, true, false, false, false, Array.Empty<string>());

                var (kind, value, conversionError) = ImportValueCoercion.Convert(field, raw, emptyAsNull);
                switch (kind)
                {
                    case ImportValueKind.Omit:
                        break;

                    case ImportValueKind.Null:
                        fields[fieldName] = null;
                        break;

                    case ImportValueKind.Error:
                        error ??= UiText.T("DataIo_Err_RowFmt", r + 1, conversionError ?? string.Empty);
                        break;

                    case ImportValueKind.Value:
                        fields[fieldName] = value;
                        break;
                }

                if (operation == DataImportOperation.Upsert &&
                    !string.IsNullOrWhiteSpace(externalIdField) &&
                    string.Equals(fieldName, externalIdField.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    externalIdValue = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
                }
            }

            if (error is null &&
                operation is DataImportOperation.Update or DataImportOperation.Delete &&
                string.IsNullOrEmpty(id))
            {
                error = UiText.T("DataIo_Err_RowIdMissingFmt", r + 1);
            }

            if (error is null &&
                operation == DataImportOperation.Upsert &&
                string.IsNullOrEmpty(externalIdValue))
            {
                error = UiText.T("DataIo_Err_RowExtIdMissingFmt", r + 1);
            }

            planned.Add(new ImportPlannedRow(r, fields, id, externalIdValue, error));
        }

        return new ImportPlan(planned);
    }

    /// <summary>送信対象（エラーなし）の行を 200 件単位に分割する。</summary>
    public static List<List<ImportPlannedRow>> ChunkSendable(IReadOnlyList<ImportPlannedRow> rows)
    {
        var batches = new List<List<ImportPlannedRow>>();
        var current = new List<ImportPlannedRow>(MaxBatchSize);

        foreach (var row in rows)
        {
            if (row.Error is not null)
            {
                continue;
            }

            current.Add(row);
            if (current.Count == MaxBatchSize)
            {
                batches.Add(current);
                current = new List<ImportPlannedRow>(MaxBatchSize);
            }
        }

        if (current.Count > 0)
        {
            batches.Add(current);
        }

        return batches;
    }

    /// <summary>composite/sobjects のリクエスト JSON を作る（attributes を各レコードの先頭に付け、Update は Id を含める）。</summary>
    public static string BuildCompositeBody(IEnumerable<ImportPlannedRow> rows, string objectType, bool includeId)
    {
        var records = new List<Dictionary<string, object?>>();

        foreach (var row in rows)
        {
            var record = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                // Composite API では attributes を必ず先頭に置く必要がある。
                ["attributes"] = new Dictionary<string, object?> { ["type"] = objectType },
            };

            if (includeId && !string.IsNullOrEmpty(row.Id))
            {
                record["Id"] = row.Id;
            }

            foreach (var (key, value) in row.Fields)
            {
                record[key] = value;
            }

            records.Add(record);
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["allOrNone"] = false,
            ["records"] = records,
        });
    }

    /// <summary>Bulk API 用の CSV（ヘッダー = API 名）と、その列名を返す。</summary>
    public static (string Csv, IReadOnlyList<string> Headers) BuildBulkCsv(
        IReadOnlyList<ImportPlannedRow> rows,
        DataImportOperation operation,
        string? externalIdField)
    {
        var fieldOrder = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var key in row.Fields.Keys)
            {
                if (seen.Add(key))
                {
                    fieldOrder.Add(key);
                }
            }
        }

        var headers = new List<string>();
        var includeId = operation is DataImportOperation.Update or DataImportOperation.Delete;
        if (includeId)
        {
            headers.Add("Id");
        }

        headers.AddRange(fieldOrder);

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(CsvExporter.Escape)));

        foreach (var row in rows)
        {
            if (row.Error is not null)
            {
                continue;
            }

            var cells = new List<string>();
            if (includeId)
            {
                cells.Add(CsvExporter.Escape(row.Id));
            }

            foreach (var name in fieldOrder)
            {
                row.Fields.TryGetValue(name, out var value);
                cells.Add(CsvExporter.Escape(FormatCell(value)));
            }

            builder.AppendLine(string.Join(",", cells));
        }

        return (builder.ToString(), headers);
    }

    /// <summary>Bulk API の値を文字列化する（Invariant・bool は true/false）。</summary>
    public static string? FormatCell(object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
