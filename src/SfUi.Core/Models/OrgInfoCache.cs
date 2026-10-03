namespace SfUi.Core;

/// <summary>組織ごとの組織情報キャッシュ（data/orginfo/&lt;orgKey&gt;.json）。</summary>
public sealed class OrgInfoCache
{
    public int SchemaVersion { get; set; } = OrgInfoCacheStore.CurrentSchemaVersion;

    public OrgInfoCacheOrg? Org { get; set; }

    public Dictionary<string, OrgInfoSection> Sections { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>キャッシュファイルに記録する組織メタ情報（表示用の控え。認証情報は保存しない）。</summary>
public sealed class OrgInfoCacheOrg
{
    public string? OrgId { get; set; }

    public string? Username { get; set; }

    public string? Alias { get; set; }

    public string? InstanceUrl { get; set; }

    public bool IsSandbox { get; set; }
}
