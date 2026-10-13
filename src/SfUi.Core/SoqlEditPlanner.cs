namespace SfUi.Core;

/// <summary>組織へ反映する 1 行分の編集内容。</summary>
public sealed record SoqlEditRow(int RowIndex, string? RecordId, IReadOnlyDictionary<string, string?> Edits);

/// <summary>反映プランの 1 行（Planned = 送信内容 / Error = 送信しない理由）。</summary>
public sealed record SoqlEditPlanEntry(int RowIndex, ImportPlannedRow? Planned, string? Error);

/// <summary>編集内容 → composite/sobjects へ送るレコードの組み立て。</summary>
public static class SoqlEditPlanner
{
    /// <summary>
    /// 行ごとの編集内容を describe に基づいて送信値へ変換する。
    /// Id が無い行・更新できない項目・型変換エラーは Error として返し、送信しない。
    /// </summary>
    public static IReadOnlyList<SoqlEditPlanEntry> Plan(
        IReadOnlyList<SoqlEditRow> rows,
        IReadOnlyDictionary<string, DataIoField>? fields)
    {
        var entries = new List<SoqlEditPlanEntry>(rows.Count);

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.RecordId))
            {
                entries.Add(new SoqlEditPlanEntry(row.RowIndex, null, UiText.T("Soql_Err_RowIdMissing")));
                continue;
            }

            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            string? error = null;

            foreach (var (name, raw) in row.Edits)
            {
                if (fields is not null && fields.TryGetValue(name, out var field))
                {
                    if (!field.Updateable)
                    {
                        error ??= UiText.T("Soql_Err_FieldNotUpdateableFmt", name);
                        continue;
                    }

                    var (kind, value, conversionError) = ImportValueCoercion.Convert(field, raw, emptyAsNull: true);
                    switch (kind)
                    {
                        case ImportValueKind.Error:
                            error ??= conversionError ?? UiText.T("DataIo_Err_NoResponse");
                            break;
                        case ImportValueKind.Omit:
                            break;
                        case ImportValueKind.Null:
                            values[name] = null;
                            break;
                        default:
                            values[name] = value;
                            break;
                    }

                    continue;
                }

                // describe を取得できない項目は文字列のまま送る（空欄は null = クリア）
                values[name] = string.IsNullOrEmpty(raw) ? null : raw;
            }

            if (error is not null)
            {
                entries.Add(new SoqlEditPlanEntry(row.RowIndex, null, error));
                continue;
            }

            entries.Add(new SoqlEditPlanEntry(row.RowIndex, new ImportPlannedRow(row.RowIndex, values, row.RecordId, null, null), null));
        }

        return entries;
    }
}
