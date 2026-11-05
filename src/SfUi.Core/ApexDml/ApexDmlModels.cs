namespace SfUi.Core;

/// <summary>匿名 Apex の DML 文生成で扱う操作種別。</summary>
public static class ApexDmlKinds
{
    public const string Insert = "insert";
    public const string Update = "update";
    public const string Delete = "delete";

    public static IReadOnlyList<string> All { get; } = new[] { Insert, Update, Delete };

    public static bool IsValid(string? kind) =>
        All.Any(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase));

    public static string ToLabel(string? kind) => UiText.T(kind?.ToLowerInvariant() switch
    {
        Update => "Dml_KindUpdate",
        Delete => "Dml_KindDelete",
        _ => "Dml_KindInsert",
    });

    /// <summary>項目（値）の入力を伴う操作か。</summary>
    public static bool HasFields(string? kind) =>
        !string.Equals(kind, Delete, StringComparison.OrdinalIgnoreCase);

    /// <summary>レコード Id が必要な操作か。</summary>
    public static bool NeedsRecordId(string? kind) =>
        !string.Equals(kind, Insert, StringComparison.OrdinalIgnoreCase);
}

/// <summary>生成に渡す 1 項目分の値。</summary>
public sealed record ApexDmlFieldValue(string FieldName, string Type, string? Value);

/// <summary>DML 文の生成要求。</summary>
public sealed record ApexDmlRequest(
    string ObjectName,
    string Kind,
    string? RecordId,
    IReadOnlyList<ApexDmlFieldValue> Fields)
{
    public bool IsInsert => string.Equals(Kind, ApexDmlKinds.Insert, StringComparison.OrdinalIgnoreCase);

    public bool IsDelete => string.Equals(Kind, ApexDmlKinds.Delete, StringComparison.OrdinalIgnoreCase);
}
