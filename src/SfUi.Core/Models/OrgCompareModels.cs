namespace SfUi.Core;

/// <summary>組織比較カテゴリの種別。</summary>
public enum OrgCompareKind
{
    /// <summary>行集合が固定の項目（概要・設定）。行 Id = 項目 ID で突合する。</summary>
    Items,

    /// <summary>行 Id または列（API 名など）で突合する行集合（OWD・各レコード系）。</summary>
    Keyed,

    /// <summary>OrgInfoCatalog の統計（算出元セクションから計算）。</summary>
    Stats,
}

/// <summary>組織比較のカテゴリ定義（タブ 1 つ分）。</summary>
/// <param name="Id">カテゴリ ID（セクション ID または "compare:stats"）。</param>
/// <param name="TitleKey">タブタイトルの UiText キー。</param>
/// <param name="Kind">種別。</param>
/// <param name="SectionId">対象セクション（統計は null）。</param>
/// <param name="KeyColumns">突合キーに使うセルキー（空 = 行 Id。複数指定は "." 連結の複合キー）。</param>
/// <param name="DisplayColumns">セル表示・値比較に使うセルキー。</param>
/// <param name="KeyDisplayColumn">1 列目の表示に使うセルキー（null = キー文字列をそのまま表示）。</param>
public sealed record OrgCompareCategory(
    string Id,
    string TitleKey,
    OrgCompareKind Kind,
    string? SectionId,
    IReadOnlyList<string> KeyColumns,
    IReadOnlyList<string> DisplayColumns,
    string? KeyDisplayColumn);

/// <summary>組織比較のカテゴリカタログ（タブの並び順もここで定義する）。</summary>
public static class OrgCompareCategories
{
    /// <summary>統計カテゴリの ID（セクションを持たない）。</summary>
    public const string StatsCategoryId = "compare:stats";

    private static readonly string[] None = Array.Empty<string>();

    public static IReadOnlyList<OrgCompareCategory> All { get; } = new OrgCompareCategory[]
    {
        new(OrgInfoSections.Overview, "OrgInfo_Tab_Overview", OrgCompareKind.Items, OrgInfoSections.Overview,
            None, new[] { "value" }, "item"),
        new(OrgInfoSections.Settings, "OrgInfo_Tab_Settings", OrgCompareKind.Items, OrgInfoSections.Settings,
            None, new[] { "value" }, "item"),
        new(OrgInfoSections.Owds, "OrgInfo_Tab_Owds", OrgCompareKind.Keyed, OrgInfoSections.Owds,
            None, new[] { "internal", "external" }, "target"),
        new(StatsCategoryId, "OrgInfo_Catalog_Stats", OrgCompareKind.Stats, null,
            None, None, null),
        new(OrgInfoSections.Users, "OrgInfo_Tab_Users", OrgCompareKind.Keyed, OrgInfoSections.Users,
            new[] { "username" }, new[] { "name", "profile", "role", "active" }, null),
        new(OrgInfoSections.Profiles, "OrgInfo_Tab_Profiles", OrgCompareKind.Keyed, OrgInfoSections.Profiles,
            new[] { "name" }, new[] { "userType", "activeUsers" }, null),
        new(OrgInfoSections.PermissionSets, "OrgInfo_Tab_PermissionSets", OrgCompareKind.Keyed, OrgInfoSections.PermissionSets,
            new[] { "apiName" }, new[] { "label", "assignedUsers" }, null),
        new(OrgInfoSections.Roles, "OrgInfo_Tab_Roles", OrgCompareKind.Keyed, OrgInfoSections.Roles,
            new[] { "developerName" }, new[] { "parentRole" }, null),
        new(OrgInfoSections.Objects, "OrgInfo_Tab_Objects", OrgCompareKind.Keyed, OrgInfoSections.Objects,
            new[] { "apiName" }, new[] { "label", "kind", "internalOwds", "externalOwds" }, null),
        new(OrgInfoSections.ApexClasses, "OrgInfo_Tab_ApexClasses", OrgCompareKind.Keyed, OrgInfoSections.ApexClasses,
            new[] { "name" }, new[] { "apiVersion", "status", "valid" }, null),
        new(OrgInfoSections.ApexTriggers, "OrgInfo_Tab_ApexTriggers", OrgCompareKind.Keyed, OrgInfoSections.ApexTriggers,
            new[] { "name" }, new[] { "apiVersion", "status", "valid" }, null),
        new(OrgInfoSections.Flows, "OrgInfo_Tab_Flows", OrgCompareKind.Keyed, OrgInfoSections.Flows,
            new[] { "apiName" }, new[] { "label", "processType", "active" }, null),
        new(OrgInfoSections.RecordTypes, "OrgInfo_Tab_RecordTypes", OrgCompareKind.Keyed, OrgInfoSections.RecordTypes,
            new[] { "sobject", "developerName" }, new[] { "name" }, null),
        new(OrgInfoSections.ScheduledJobs, "OrgInfo_Tab_ScheduledJobs", OrgCompareKind.Keyed, OrgInfoSections.ScheduledJobs,
            new[] { "name" }, new[] { "jobType", "state", "nextFireTime" }, null),
        new(OrgInfoSections.ConnectedApps, "OrgInfo_Tab_ConnectedApps", OrgCompareKind.Keyed, OrgInfoSections.ConnectedApps,
            new[] { "name" }, new[] { "lastModified" }, null),
        new(OrgInfoSections.InstalledPackages, "OrgInfo_Tab_InstalledPackages", OrgCompareKind.Keyed, OrgInfoSections.InstalledPackages,
            new[] { "name" }, new[] { "namespace", "version" }, null),
        new(OrgInfoSections.Currencies, "OrgInfo_Tab_Currencies", OrgCompareKind.Keyed, OrgInfoSections.Currencies,
            new[] { "isoCode" }, new[] { "name", "active", "conversionRate" }, null),
        // 時系列データ（差分が多くなりがち。タブ内検索・差分のみフィルタで絞る）
        new(OrgInfoSections.LoginHistory, "OrgInfo_Tab_LoginHistory", OrgCompareKind.Keyed, OrgInfoSections.LoginHistory,
            new[] { "user", "loginTime" }, new[] { "loginType", "status", "sourceIp" }, null),
        new(OrgInfoSections.SetupAuditTrail, "OrgInfo_Tab_AuditTrail", OrgCompareKind.Keyed, OrgInfoSections.SetupAuditTrail,
            new[] { "datetime" }, new[] { "user", "action", "section", "detail" }, null),
    };

    /// <summary>カテゴリ ID からカテゴリを返す（不明は null）。</summary>
    public static OrgCompareCategory? Find(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
    }

    /// <summary>カテゴリが必要とするセクション ID（統計は算出元の全セクション。順序は Stats の定義順）。</summary>
    public static IReadOnlyList<string> RequiredSectionIds(OrgCompareCategory category)
    {
        if (category.Kind != OrgCompareKind.Stats)
        {
            return category.SectionId is { Length: > 0 } id ? new[] { id } : Array.Empty<string>();
        }

        var ids = new List<string>();
        foreach (var stat in OrgInfoCatalog.Stats)
        {
            var source = OrgInfoCatalog.StatSourceSection(stat.Id);
            if (source is not null && !ids.Contains(source, StringComparer.Ordinal))
            {
                ids.Add(source);
            }
        }

        return ids;
    }
}

/// <summary>比較列（組織 1 つ分）。InstanceUrl はセルリンクのフォールバック（セクション Setup URL）に使う。</summary>
public sealed record OrgCompareOrgColumn(string OrgKey, string DisplayName, string Username, string? InstanceUrl);

/// <summary>比較セルの状態。</summary>
public enum OrgCompareCellState
{
    /// <summary>値あり。</summary>
    Value,

    /// <summary>その組織にデータが存在しない（キー欠落）。</summary>
    Missing,

    /// <summary>セクション未取得（比較時に自動取得される）。</summary>
    NotFetched,

    /// <summary>取得成功に失敗（キャッシュも無い）。</summary>
    Failed,
}

/// <summary>比較セル（1 組織分）。RawValues = 比較列の生値（言語非依存・差分判定用）。</summary>
public sealed record OrgCompareCell(
    OrgCompareCellState State,
    string? Text,
    string? Link,
    IReadOnlyList<string?> RawValues);

/// <summary>よく使う状態のセル（テキストは表示時にローカライズする）。</summary>
public static class OrgCompareCells
{
    public static OrgCompareCell Missing { get; } = new(OrgCompareCellState.Missing, null, null, Array.Empty<string?>());

    public static OrgCompareCell NotFetched { get; } = new(OrgCompareCellState.NotFetched, null, null, Array.Empty<string?>());

    public static OrgCompareCell Failed { get; } = new(OrgCompareCellState.Failed, null, null, Array.Empty<string?>());
}

/// <summary>比較行（Key = 内部突合キー、Label = 1 列目の表示、Cells = 組織順のセル）。</summary>
public sealed record OrgCompareRow(string Key, string Label, bool IsDiff, IReadOnlyList<OrgCompareCell> Cells);

/// <summary>カテゴリ 1 つ分の比較結果。</summary>
public sealed record OrgCompareTable(
    string CategoryId,
    IReadOnlyList<OrgCompareOrgColumn> Orgs,
    IReadOnlyList<OrgCompareRow> Rows)
{
    /// <summary>差分のある行数。</summary>
    public int DiffCount => Rows.Count(r => r.IsDiff);
}

/// <summary>1 組織 × 1 セクションの取得結果（BuildTable の入力。State は Value / NotFetched / Failed）。</summary>
public sealed record OrgCompareSource(string OrgKey, string SectionId, OrgCompareCellState State, OrgInfoSection? Section);
