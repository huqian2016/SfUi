using System.Globalization;

namespace SfUi.Core;

/// <summary>
/// マイ設定タブの項目カタログ（概要・設定・OWD のスカラー値 + 統計）と、キャッシュからの値解決。
/// 項目 ID は "グループ:項目" 形式（例: overview:orgName / settings:uiSkin / owd:Account / stat:activeUsers）。
/// </summary>
public static class OrgInfoCatalog
{
    public const string GroupOverview = "overview";
    public const string GroupSettings = "settings";
    public const string GroupOwd = "owd";
    public const string GroupStat = "stat";

    /// <summary>カタログ項目（Id / グループ / ラベルキー）。</summary>
    public sealed record Item(string Id, string Group, string LabelKey);

    /// <summary>統計項目（キャッシュから算出。ラベルキーは OrgInfo_Stat_*）。</summary>
    public sealed record StatItem(string Id, string LabelKey);

    public static IReadOnlyList<StatItem> Stats { get; } = new StatItem[]
    {
        new("totalUsers", "OrgInfo_Stat_TotalUsers"),
        new("activeUsers", "OrgInfo_Stat_ActiveUsers"),
        new("profiles", "OrgInfo_Stat_Profiles"),
        new("permissionSets", "OrgInfo_Stat_PermissionSets"),
        new("roles", "OrgInfo_Stat_Roles"),
        new("objects", "OrgInfo_Stat_Objects"),
        new("customObjects", "OrgInfo_Stat_CustomObjects"),
    };

    public static IReadOnlyList<Item> All { get; } = BuildItems();

    private static List<Item> BuildItems()
    {
        var items = new List<Item>();
        foreach (var item in OrgInfoSections.OverviewItems)
        {
            items.Add(new Item($"{GroupOverview}:{item.Id}", GroupOverview, item.LabelKey));
        }

        foreach (var item in OrgInfoSections.SettingsItems)
        {
            items.Add(new Item($"{GroupSettings}:{item.Id}", GroupSettings, item.LabelKey));
        }

        foreach (var target in OrgInfoSections.OwdDefaultTargets)
        {
            items.Add(new Item($"{GroupOwd}:{target.Id}", GroupOwd, target.LabelKey));
        }

        foreach (var stat in Stats)
        {
            items.Add(new Item($"{GroupStat}:{stat.Id}", GroupStat, stat.LabelKey));
        }

        return items;
    }

    public static Item? Find(string? itemId) =>
        string.IsNullOrEmpty(itemId) ? null : All.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));

    public static string? LabelKeyFor(string? itemId) => Find(itemId)?.LabelKey;

    public static string? GroupFor(string? itemId) => Find(itemId)?.Group;

    /// <summary>ピッカーのグループ見出しに使うセクションタイトルのキー。</summary>
    public static string GroupTitleKey(string group) => group switch
    {
        GroupOverview => "OrgInfo_Tab_Overview",
        GroupSettings => "OrgInfo_Tab_Settings",
        GroupOwd => "OrgInfo_Tab_Owds",
        GroupStat => "OrgInfo_Catalog_Stats",
        _ => group,
    };

    /// <summary>項目の値解決結果（値・リンク・参照元セクション・統計かどうか）。</summary>
    public sealed record Value(string? Text, string? Link, string? SourceSectionId, bool IsStat);

    /// <summary>参照元セクション ID（再取得の対象）。統計は算出元のセクションを返す。</summary>
    public static IReadOnlyList<string> SourceSectionIds(string itemId)
    {
        var item = Find(itemId);
        if (item is null)
        {
            return Array.Empty<string>();
        }

        return item.Group switch
        {
            GroupOverview => new[] { OrgInfoSections.Overview },
            GroupSettings => new[] { OrgInfoSections.Settings },
            GroupOwd => new[] { OrgInfoSections.Owds },
            GroupStat => StatSourceSection(Suffix(itemId)) is { } source ? new[] { source } : Array.Empty<string>(),
            _ => Array.Empty<string>(),
        };
    }

    /// <summary>統計項目の算出元セクション（不明は null）。</summary>
    public static string? StatSourceSection(string statId) => statId switch
    {
        "totalUsers" or "activeUsers" => OrgInfoSections.Users,
        "profiles" => OrgInfoSections.Profiles,
        "permissionSets" => OrgInfoSections.PermissionSets,
        "roles" => OrgInfoSections.Roles,
        "objects" or "customObjects" => OrgInfoSections.Objects,
        _ => null,
    };

    /// <summary>キャッシュから項目の値を解決する（未取得は null 値のまま返す）。</summary>
    public static Value? Resolve(string itemId, Func<string, OrgInfoSection?> getSection)
    {
        var item = Find(itemId);
        if (item is null)
        {
            return null;
        }

        var suffix = Suffix(itemId);
        switch (item.Group)
        {
            case GroupOverview:
            {
                var section = getSection(OrgInfoSections.Overview);
                var row = FindRow(section, suffix);
                return new Value(row?.Get("value"), null, OrgInfoSections.Overview, false);
            }

            case GroupSettings:
            {
                var section = getSection(OrgInfoSections.Settings);
                var row = FindRow(section, suffix);
                return new Value(row?.Get("value"), row?.Link, OrgInfoSections.Settings, false);
            }

            case GroupOwd:
            {
                var section = getSection(OrgInfoSections.Owds);
                var row = FindRow(section, "org:" + suffix);
                return new Value(row?.Get("internal"), null, OrgInfoSections.Owds, false);
            }

            case GroupStat:
                return new Value(ComputeStat(suffix, getSection), null, StatSourceSection(suffix), true);

            default:
                return null;
        }
    }

    /// <summary>統計値をキャッシュから算出する（算出元セクション未取得は null）。</summary>
    public static string? ComputeStat(string statId, Func<string, OrgInfoSection?> getSection)
    {
        static string? Count(OrgInfoSection? section) =>
            section is null ? null : section.Rows.Count.ToString(CultureInfo.InvariantCulture);

        static string? CountWhere(OrgInfoSection? section, string columnKey, string token) =>
            section is null
                ? null
                : section.Rows.Count(r => string.Equals(r.Get(columnKey), token, StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture);

        return statId switch
        {
            "totalUsers" => Count(getSection(OrgInfoSections.Users)),
            "activeUsers" => CountWhere(getSection(OrgInfoSections.Users), "active", OrgInfoTokens.True),
            "profiles" => Count(getSection(OrgInfoSections.Profiles)),
            "permissionSets" => Count(getSection(OrgInfoSections.PermissionSets)),
            "roles" => Count(getSection(OrgInfoSections.Roles)),
            "objects" => Count(getSection(OrgInfoSections.Objects)),
            "customObjects" => CountWhere(getSection(OrgInfoSections.Objects), "kind", OrgInfoTokens.Custom),
            _ => null,
        };
    }

    private static OrgInfoRow? FindRow(OrgInfoSection? section, string rowId) =>
        section?.Rows.FirstOrDefault(r => string.Equals(r.Id, rowId, StringComparison.Ordinal));

    private static string Suffix(string itemId)
    {
        var index = itemId.IndexOf(':');
        return index < 0 ? itemId : itemId[(index + 1)..];
    }
}
