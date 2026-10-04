namespace SfUi.Core;

/// <summary>権限の主体種別（プロファイル / 権限セット / 権限セットグループ）。</summary>
public enum PermissionSubjectKind
{
    /// <summary>プロファイル（PermissionSet.IsOwnedByProfile = true のレコードで表現される）。</summary>
    Profile,

    /// <summary>権限セット。</summary>
    PermissionSet,

    /// <summary>権限セットグループ。</summary>
    PermissionSetGroup,
}

/// <summary>権限の主体（プロファイル / 権限セット / 権限セットグループ）。</summary>
public sealed record PermissionSubject(
    string Id,
    PermissionSubjectKind Kind,
    string Label,
    string ApiName,
    bool IsCustom);

/// <summary>権限主体のカタログ（主体一覧 + 権限セットグループの構成）。</summary>
public sealed record PermissionCatalog(
    IReadOnlyList<PermissionSubject> Subjects,
    IReadOnlyDictionary<string, IReadOnlyList<string>> GroupComponents)
{
    public static PermissionCatalog Empty { get; } = new(
        Array.Empty<PermissionSubject>(),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    public PermissionSubject? Find(string id) =>
        Subjects.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
}

/// <summary>オブジェクトアクセス 1 行分（選択中オブジェクトに対する主体ごとの権限）。</summary>
public sealed record ObjectAccessRow(
    PermissionSubject Subject,
    bool Read,
    bool Create,
    bool Edit,
    bool Delete,
    bool ViewAllRecords,
    bool ModifyAllRecords,
    bool ViewAllFields);

/// <summary>項目アクセスの 1 セル（読取 / 編集）。</summary>
public sealed record FieldAccessCell(bool Read, bool Edit)
{
    /// <summary>いずれかの権限があるか。</summary>
    public bool Any => Read || Edit;

    public static FieldAccessCell None { get; } = new(false, false);
}

/// <summary>項目アクセスのスナップショット（主体 Id → 項目 API 名 → セル）。</summary>
public sealed record FieldAccessSnapshot(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, FieldAccessCell>> BySubject)
{
    public static FieldAccessSnapshot Empty { get; } = new(
        new Dictionary<string, IReadOnlyDictionary<string, FieldAccessCell>>(StringComparer.Ordinal));
}

/// <summary>レコードアクセスの対象ユーザー（有効ユーザー）。</summary>
public sealed record RecordAccessUser(string Id, string Name, string Username)
{
    /// <summary>表示用（名前 + ユーザー名）。</summary>
    public string Display => $"{Name} ({Username})";
}

/// <summary>対象レコードの抽出（SOQL）結果。</summary>
public sealed record RecordQueryResult(
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Records,
    IReadOnlyList<string> Columns,
    bool Truncated)
{
    public static RecordQueryResult Empty { get; } = new(
        Array.Empty<IReadOnlyDictionary<string, string?>>(),
        Array.Empty<string>(),
        false);
}

/// <summary>レコードアクセス権（UserRecordAccess）。レコード単位に作成権限は存在しない。</summary>
public sealed record UserRecordAccessFlags(bool Read, bool Edit, bool Delete, bool Transfer)
{
    public static UserRecordAccessFlags None { get; } = new(false, false, false, false);
}
