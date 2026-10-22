namespace SfUi.Core;

/// <summary>有効ユーザー（プロファイル付き）。ユーザー中心の実効アクセス表示用。</summary>
public sealed record AccessUser(string Id, string Name, string Username, string ProfileId, string ProfileName)
{
    /// <summary>表示用（名前 + ユーザー名）。</summary>
    public string Display => $"{Name} ({Username})";

    /// <summary>一覧の表示用（名前 + ユーザー名 + プロファイル）。</summary>
    public string DisplayWithProfile =>
        string.IsNullOrWhiteSpace(ProfileName) ? Display : $"{Display} · {ProfileName}";
}

/// <summary>権限セットの割り当て 1 行（権限セット グループの割り当てを含む）。</summary>
public sealed record UserPermissionAssignment(string UserId, string PermissionSetId, string? PermissionSetGroupId);

/// <summary>ユーザーの実効オブジェクト権限（プロファイル + 割当 PS / PSG の和集合）。</summary>
public sealed record EffectiveObjectAccess(
    bool Read,
    bool Create,
    bool Edit,
    bool Delete,
    bool ViewAllRecords,
    bool ModifyAllRecords,
    bool ViewAllFields)
{
    public static EffectiveObjectAccess None { get; } = new(false, false, false, false, false, false, false);
}

/// <summary>ユーザーの実効項目権限 1 行（付与元の主体も保持する）。</summary>
public sealed record EffectiveFieldAccess(
    string FieldApiName,
    string Label,
    bool Read,
    bool Edit,
    IReadOnlyList<PermissionSubject> ReadGrants,
    IReadOnlyList<PermissionSubject> EditGrants);

/// <summary>1 ユーザー分の実効アクセス。</summary>
public sealed record UserEffectiveAccess(
    AccessUser User,
    EffectiveObjectAccess Object,
    IReadOnlyList<EffectiveFieldAccess> Fields);
