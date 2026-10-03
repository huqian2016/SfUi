using System.Text;

namespace SfUi.Core;

/// <summary>
/// 組織情報の表示値変換。言語依存の表示（トークンのローカライズ、概要・OWD のラベル解決）をここに集約し、
/// グリッド表示（App）と検索（OrgInfoSearchService）で共用する。
/// </summary>
public static class OrgInfoDisplay
{
    /// <summary>セルを表示用文字列へ変換する（トークンのローカライズ、概要・OWD のラベル解決）。</summary>
    public static string? FormatCell(string sectionId, OrgInfoRow row, string columnKey, string? raw)
    {
        if (OrgInfoSections.IsFieldsSection(sectionId))
        {
            return columnKey switch
            {
                "custom" or "nillable" or "indexed" or "calculated" or "historyTracked" => FormatYesNo(raw),
                _ => raw,
            };
        }

        return (sectionId, columnKey) switch
        {
            (OrgInfoSections.Overview, "item") => ResolveOverviewItem(row),
            (OrgInfoSections.Overview, "environment") => raw switch
            {
                OrgInfoTokens.Sandbox => UiText.T("OrgInfo_Value_Sandbox"),
                OrgInfoTokens.Production => UiText.T("OrgInfo_Value_Production"),
                _ => raw,
            },
            (OrgInfoSections.Settings, "item") => ResolveSettingsItem(row),
            (OrgInfoSections.Settings, "value") => FormatSettingsValue(raw),
            (OrgInfoSections.Users, "active") => FormatYesNo(raw),
            (OrgInfoSections.Objects, "kind") => raw switch
            {
                OrgInfoTokens.Standard => UiText.T("OrgInfo_Value_Standard"),
                OrgInfoTokens.Custom => UiText.T("OrgInfo_Value_Custom"),
                _ => raw,
            },
            (OrgInfoSections.Objects, "customSetting") => FormatYesNo(raw),
            (OrgInfoSections.Owds, "target") => ResolveOwdTarget(raw),
            (OrgInfoSections.Owds, "source") => raw switch
            {
                OrgInfoTokens.OrgDefault => UiText.T("OrgInfo_Value_OrgDefault"),
                OrgInfoTokens.Object => UiText.T("OrgInfo_Value_Object"),
                _ => raw,
            },
            (OrgInfoSections.ApexClasses, "valid") => FormatYesNo(raw),
            (OrgInfoSections.ApexTriggers, "valid") => FormatYesNo(raw),
            (OrgInfoSections.Flows, "active") => FormatYesNo(raw),
            (OrgInfoSections.RecordTypes, "active") => FormatYesNo(raw),
            (OrgInfoSections.Currencies, "active") => FormatYesNo(raw),
            _ => raw,
        };
    }

    /// <summary>行全体の検索用テキスト（列ラベル + 表示値 + 要約の小文字連結）。項目名も値も対象にする。</summary>
    public static string BuildSearchText(string sectionId, IReadOnlyList<OrgInfoColumn> columns, OrgInfoRow row)
    {
        var builder = new StringBuilder();
        foreach (var column in columns)
        {
            builder.Append(column.Label).Append(' ');
            builder.Append(FormatCell(sectionId, row, column.Key, row.Get(column.Key))).Append(' ');
        }

        builder.Append(row.Summary);
        return builder.ToString().ToLowerInvariant();
    }

    /// <summary>検索テキストがすべての語を含むか（AND）。</summary>
    public static bool Matches(string searchText, IReadOnlyList<string> lowerTerms) =>
        lowerTerms.Count == 0 || lowerTerms.All(t => searchText.Contains(t, StringComparison.Ordinal));

    /// <summary>値トークンを汎用に表示文字列へ変換する（マイ設定タブなどの表示用）。</summary>
    public static string? FormatValueToken(string? raw) => raw switch
    {
        OrgInfoTokens.LinkOnly => UiText.T("OrgInfo_Settings_LinkOnly"),
        OrgInfoTokens.True or OrgInfoTokens.False => FormatYesNo(raw),
        OrgInfoTokens.Sandbox => UiText.T("OrgInfo_Value_Sandbox"),
        OrgInfoTokens.Production => UiText.T("OrgInfo_Value_Production"),
        OrgInfoTokens.Standard => UiText.T("OrgInfo_Value_Standard"),
        OrgInfoTokens.Custom => UiText.T("OrgInfo_Value_Custom"),
        OrgInfoTokens.OrgDefault => UiText.T("OrgInfo_Value_OrgDefault"),
        OrgInfoTokens.Object => UiText.T("OrgInfo_Value_Object"),
        _ => raw,
    };

    private static string? ResolveOverviewItem(OrgInfoRow row)
    {
        var labelKey = OrgInfoSections.OverviewLabelKey(row.Id);
        return labelKey is null ? row.Id : UiText.T(labelKey);
    }

    private static string? ResolveSettingsItem(OrgInfoRow row)
    {
        var labelKey = OrgInfoSections.SettingsLabelKey(row.Id);
        return labelKey is null ? row.Id : UiText.T(labelKey);
    }

    private static string FormatSettingsValue(string? raw) =>
        raw == OrgInfoTokens.LinkOnly ? UiText.T("OrgInfo_Settings_LinkOnly") : FormatYesNo(raw);

    private static string? ResolveOwdTarget(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var labelKey = OrgInfoSections.OwdTargetLabelKey(raw);
        return labelKey is null ? raw : UiText.T(labelKey);
    }

    private static string FormatYesNo(string? raw) => raw switch
    {
        OrgInfoTokens.True => UiText.T("OrgInfo_Value_Yes"),
        OrgInfoTokens.False => UiText.T("OrgInfo_Value_No"),
        _ => raw ?? string.Empty,
    };
}
