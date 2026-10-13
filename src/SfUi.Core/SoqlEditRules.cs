namespace SfUi.Core;

/// <summary>SOQL 結果グリッドの編集可否ルール。</summary>
public static class SoqlEditRules
{
    /// <summary>組織へ直接更新できないシステム項目。</summary>
    private static readonly HashSet<string> SystemReadOnlyFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id",
        "CreatedDate",
        "CreatedById",
        "LastModifiedDate",
        "LastModifiedById",
        "SystemModstamp",
        "IsDeleted",
        "LastViewedDate",
        "LastReferencedDate",
        "LastActivityDate",
    };

    /// <summary>システム項目（Id・作成/更新日時など）か。</summary>
    public static bool IsSystemField(string columnName) => SystemReadOnlyFields.Contains(columnName);

    /// <summary>列を読み取り専用にするか（システム項目・参照項目・配列/オブジェクト値）。</summary>
    public static bool IsReadOnlyColumn(string columnName, bool hasJsonLikeValues) =>
        IsSystemField(columnName)
        || columnName.Contains('.')
        || hasJsonLikeValues;

    /// <summary>セル値が JSON（配列・オブジェクト）の文字列か。</summary>
    public static bool LooksLikeJson(string? value)
    {
        var text = value?.TrimStart();
        return text is { Length: > 0 } && (text[0] == '[' || text[0] == '{');
    }
}
