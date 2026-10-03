namespace SfUi.Core;

/// <summary>CSV ヘッダーと describe からの自動マッピング・候補項目の決定・事前バリデーション。</summary>
public static class ImportFieldMatcher
{
    public const string IdField = "Id";

    /// <summary>Id 列マッピング用のプレースホルダー（describe 上 Id は updateable=false のため）。</summary>
    public static DataIoField IdPlaceholder { get; } =
        new("Id", "Id", "id", false, false, false, false, false, false, Array.Empty<string>());

    /// <summary>操作に応じたマッピング候補項目（Update / Delete は Id を先頭に追加）。</summary>
    public static IReadOnlyList<DataIoField> CandidateFields(DataIoObjectDescribe describe, DataImportOperation operation, string? externalIdField)
    {
        var result = new List<DataIoField>();

        if (operation is DataImportOperation.Update or DataImportOperation.Delete)
        {
            result.Add(IdPlaceholder);
        }

        if (operation == DataImportOperation.Delete)
        {
            return result;
        }

        foreach (var field in describe.Fields)
        {
            if (string.Equals(field.Name, IdField, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var include = operation switch
            {
                DataImportOperation.Insert or DataImportOperation.Upsert => field.Createable,
                DataImportOperation.Update => field.Updateable,
                _ => false,
            };

            // Upsert の外部 ID 項目は updateable/createable に関わらずマッピングできるようにする
            if (!include && operation == DataImportOperation.Upsert && IsField(field, externalIdField))
            {
                include = true;
            }

            if (include)
            {
                result.Add(field);
            }
        }

        return result;
    }

    /// <summary>外部 ID として指定できる項目（describe の externalId=true）。</summary>
    public static IReadOnlyList<DataIoField> ExternalIdFields(DataIoObjectDescribe describe) =>
        describe.Fields.Where(f => f.ExternalId).ToList();

    /// <summary>ヘッダーから自動マッピングを提案する（API 名 → ラベルの順で照合。2 列目以降の重複は未マッピング）。</summary>
    public static IReadOnlyList<ImportColumnMapping> Suggest(
        IReadOnlyList<string> headers,
        DataIoObjectDescribe describe,
        DataImportOperation operation,
        string? externalIdField)
    {
        var candidates = CandidateFields(describe, operation, externalIdField);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mappings = new List<ImportColumnMapping>(headers.Count);

        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i].Trim();
            DataIoField? match = null;

            if (operation is DataImportOperation.Update or DataImportOperation.Delete &&
                string.Equals(header, IdField, StringComparison.OrdinalIgnoreCase))
            {
                match = IdPlaceholder;
            }

            if (match is null)
            {
                match = candidates.FirstOrDefault(c => string.Equals(c.Name, header, StringComparison.OrdinalIgnoreCase))
                    ?? candidates.FirstOrDefault(c => string.Equals(c.Label, header, StringComparison.OrdinalIgnoreCase));
            }

            if (match is not null && !used.Add(match.Name))
            {
                match = null;
            }

            mappings.Add(new ImportColumnMapping(i, headers[i], match?.Name, Include: match is not null));
        }

        return mappings;
    }

    /// <summary>指定フィールドがマッピング済みか。</summary>
    public static bool MapsField(IReadOnlyList<ImportColumnMapping> mappings, string? fieldName) =>
        !string.IsNullOrEmpty(fieldName) &&
        mappings.Any(m => m.IsMapped && string.Equals(m.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));

    /// <summary>実行前バリデーション（問題があればローカライズ済みメッセージ、なければ null）。</summary>
    public static string? Validate(
        IReadOnlyList<ImportColumnMapping> mappings,
        int rowCount,
        DataImportOperation operation,
        string? externalIdField)
    {
        if (rowCount == 0)
        {
            return UiText.T("DataImport_Err_NoRows");
        }

        if (!mappings.Any(m => m.IsMapped))
        {
            return UiText.T("DataImport_Err_NoMapped");
        }

        switch (operation)
        {
            case DataImportOperation.Update:
            case DataImportOperation.Delete:
                if (!MapsField(mappings, IdField))
                {
                    return UiText.T("DataImport_Err_IdRequired");
                }

                break;

            case DataImportOperation.Upsert:
                if (string.IsNullOrWhiteSpace(externalIdField))
                {
                    return UiText.T("DataImport_Err_ExtIdRequired");
                }

                if (!MapsField(mappings, externalIdField))
                {
                    return UiText.T("DataImport_Err_ExtIdMapRequired");
                }

                break;
        }

        return null;
    }

    /// <summary>Insert 時の必須項目で未マッピングのもの（警告表示用）。</summary>
    public static IReadOnlyList<DataIoField> MissingRequiredFields(
        IReadOnlyList<ImportColumnMapping> mappings,
        DataIoObjectDescribe describe) =>
        describe.Fields.Where(f => f.RequiredForInsert && !MapsField(mappings, f.Name)).ToList();

    private static bool IsField(DataIoField field, string? name) =>
        !string.IsNullOrWhiteSpace(name) && string.Equals(field.Name, name.Trim(), StringComparison.OrdinalIgnoreCase);
}
