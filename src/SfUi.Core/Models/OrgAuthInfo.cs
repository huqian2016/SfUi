namespace SfUi.Core;

/// <summary>組織の認証情報（アクセストークン等）。</summary>
public sealed record OrgAuthInfo(
    string TargetOrg,
    string? Username,
    string? InstanceUrl,
    string AccessToken,
    string? ApiVersion,
    DateTimeOffset FetchedAt)
{
    /// <summary>キャッシュが指定期間内か。</summary>
    public bool IsFresh(TimeSpan maxAge) => DateTimeOffset.Now - FetchedAt < maxAge;
}
