namespace SfUi.Core;

/// <summary>マイ設定（カスタムタブ）の定義。data/orginfo/preferences.json に組織別で保存する。</summary>
public sealed class OrgInfoPreferences
{
    public int SchemaVersion { get; set; } = OrgInfoPreferencesStore.CurrentSchemaVersion;

    public Dictionary<string, OrgInfoOrgPreferences> Orgs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>1 組織分のマイ設定（カスタムタブの一覧）。</summary>
public sealed class OrgInfoOrgPreferences
{
    public List<OrgInfoCustomTab> Tabs { get; set; } = new();
}

/// <summary>カスタムタブ定義（Id / タブ名 / 項目 ID の並び）。</summary>
public sealed class OrgInfoCustomTab
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>OrgInfoCatalog の項目 ID（例: overview:orgName）。</summary>
    public List<string> Items { get; set; } = new();
}
