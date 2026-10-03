namespace SfUi.Core;

/// <summary>組織情報検索のヒット 1 件（表示用文字列へ解決済み）。</summary>
public sealed record OrgInfoSearchHit(
    string SectionId,
    string SectionTitle,
    string RowId,
    string RowSummary,
    string FieldLabel,
    string Value);

/// <summary>取得済みセクションを対象にした全タブ横断検索（列名と値の両方を対象、スペース区切りは AND）。</summary>
public sealed class OrgInfoSearchService
{
    /// <summary>戻すヒットの上限。</summary>
    public const int MaxResults = 1000;

    /// <summary>複数セクションを横断して検索する（未取得セクションは対象外）。</summary>
    public IReadOnlyList<OrgInfoSearchHit> Search(IEnumerable<OrgInfoSection> sections, string? query)
    {
        var terms = ParseTerms(query);
        if (terms.Count == 0)
        {
            return Array.Empty<OrgInfoSearchHit>();
        }

        var hits = new List<OrgInfoSearchHit>();
        foreach (var section in sections)
        {
            if (!section.HasData)
            {
                continue;
            }

            AddSectionHits(section, terms, hits);
            if (hits.Count >= MaxResults)
            {
                break;
            }
        }

        return hits;
    }

    /// <summary>セクションの表示名（オブジェクト項目は「オブジェクト項目: API 名」）。</summary>
    public static string SectionTitle(string sectionId)
    {
        var definition = OrgInfoSections.Find(sectionId);
        if (definition is not null)
        {
            return UiText.T(definition.TitleKey);
        }

        if (OrgInfoSections.IsFieldsSection(sectionId))
        {
            var apiName = sectionId[OrgInfoSections.FieldsPrefix.Length..];
            return $"{UiText.T(OrgInfoSections.FieldsTitleKey)}: {apiName}";
        }

        return sectionId;
    }

    /// <summary>検索語を小文字のリストにする（空語は無視）。</summary>
    public static List<string> ParseTerms(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<string>();
        }

        return query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToList();
    }

    private static void AddSectionHits(OrgInfoSection section, List<string> terms, List<OrgInfoSearchHit> hits)
    {
        var title = SectionTitle(section.Id);
        foreach (var row in section.Rows)
        {
            var searchText = OrgInfoDisplay.BuildSearchText(section.Id, section.Columns, row);
            if (!OrgInfoDisplay.Matches(searchText, terms))
            {
                continue;
            }

            var (fieldLabel, value) = FindMatchDetails(section, row, terms);
            hits.Add(new OrgInfoSearchHit(section.Id, title, row.Id, row.Summary, fieldLabel, value));
            if (hits.Count >= MaxResults)
            {
                return;
            }
        }
    }

    private static (string Field, string Value) FindMatchDetails(OrgInfoSection section, OrgInfoRow row, List<string> terms)
    {
        foreach (var column in section.Columns)
        {
            var label = column.Label;
            var value = OrgInfoDisplay.FormatCell(section.Id, row, column.Key, row.Get(column.Key)) ?? string.Empty;
            var text = (label + " " + value).ToLowerInvariant();
            if (terms.Any(t => text.Contains(t, StringComparison.Ordinal)))
            {
                return (label, value);
            }
        }

        return (string.Empty, row.Summary);
    }
}
