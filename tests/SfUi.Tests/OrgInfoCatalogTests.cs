using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgInfoCatalogTests
{
    private static OrgInfoRow Row(string id, params (string Key, string? Value)[] cells)
    {
        var row = new OrgInfoRow { Id = id, Summary = id };
        foreach (var (key, value) in cells)
        {
            row.Cells[key] = value;
        }

        return row;
    }

    private static OrgInfoSection Section(string id, IEnumerable<OrgInfoRow> rows) =>
        OrgInfoSection.Create(id, OrgInfoSections.ColumnsFor(id), rows.ToList(), DateTimeOffset.Now, 1);

    private static Func<string, OrgInfoSection?> Provider(params OrgInfoSection[] sections)
    {
        var map = sections.ToDictionary(s => s.Id, StringComparer.Ordinal);
        return id => map.TryGetValue(id, out var section) ? section : null;
    }

    [Fact]
    public void ItemIds_AreUnique()
    {
        var ids = OrgInfoCatalog.All.Select(i => i.Id).ToList();

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CatalogLabelKeys_AreDefinedInBothLanguages()
    {
        foreach (var key in OrgInfoCatalog.All.Select(i => i.LabelKey).Distinct(StringComparer.Ordinal))
        {
            Assert.Contains(key, UiText.EnglishKeys);
            Assert.Contains(key, UiText.JapaneseKeys);
        }

        foreach (var group in new[] { OrgInfoCatalog.GroupOverview, OrgInfoCatalog.GroupSettings, OrgInfoCatalog.GroupOwd, OrgInfoCatalog.GroupStat })
        {
            var key = OrgInfoCatalog.GroupTitleKey(group);
            Assert.Contains(key, UiText.EnglishKeys);
            Assert.Contains(key, UiText.JapaneseKeys);
        }
    }

    [Fact]
    public void Find_ResolvesItemsAndLabelKeys()
    {
        Assert.Equal("OrgInfo_Item_OrgName", OrgInfoCatalog.LabelKeyFor("overview:orgName"));
        Assert.Equal(OrgInfoCatalog.GroupOwd, OrgInfoCatalog.GroupFor("owd:Account"));
        Assert.Equal("OrgInfo_Stat_ActiveUsers", OrgInfoCatalog.LabelKeyFor("stat:activeUsers"));
        Assert.Null(OrgInfoCatalog.Find("nope"));
    }

    [Fact]
    public void SourceSectionIds_MapsGroups()
    {
        Assert.Equal(new[] { OrgInfoSections.Overview }, OrgInfoCatalog.SourceSectionIds("overview:orgName"));
        Assert.Equal(new[] { OrgInfoSections.Settings }, OrgInfoCatalog.SourceSectionIds("settings:uiSkin"));
        Assert.Equal(new[] { OrgInfoSections.Owds }, OrgInfoCatalog.SourceSectionIds("owd:Account"));
        Assert.Equal(new[] { OrgInfoSections.Users }, OrgInfoCatalog.SourceSectionIds("stat:activeUsers"));
        Assert.Equal(new[] { OrgInfoSections.Objects }, OrgInfoCatalog.SourceSectionIds("stat:customObjects"));
        Assert.Empty(OrgInfoCatalog.SourceSectionIds("nope"));
    }

    [Fact]
    public void Resolve_OverviewAndSettingsAndOwdValues()
    {
        var settingsRow = Row("sessionSettings", ("value", OrgInfoTokens.LinkOnly));
        settingsRow.Link = "https://x/lightning/setup/SessionSettings/home";
        var provider = Provider(
            Section(OrgInfoSections.Overview, new[] { Row("orgName", ("value", "HKS Demo")) }),
            Section(OrgInfoSections.Settings, new[] { settingsRow }),
            Section(OrgInfoSections.Owds, new[] { Row("org:Account", ("internal", "Private")) }));

        var overview = OrgInfoCatalog.Resolve("overview:orgName", provider);
        Assert.Equal("HKS Demo", overview!.Text);
        Assert.Equal(OrgInfoSections.Overview, overview.SourceSectionId);

        var settings = OrgInfoCatalog.Resolve("settings:sessionSettings", provider);
        Assert.Equal(OrgInfoTokens.LinkOnly, settings!.Text);
        Assert.Equal("https://x/lightning/setup/SessionSettings/home", settings.Link);

        var owd = OrgInfoCatalog.Resolve("owd:Account", provider);
        Assert.Equal("Private", owd!.Text);
        Assert.Equal(OrgInfoSections.Owds, owd.SourceSectionId);
    }

    [Fact]
    public void Resolve_MissingSection_ReturnsNullText()
    {
        var value = OrgInfoCatalog.Resolve("overview:orgName", _ => null);

        Assert.NotNull(value);
        Assert.Null(value!.Text);
    }

    [Fact]
    public void ComputeStats_CountsRowsAndCustomObjects()
    {
        var provider = Provider(
            Section(OrgInfoSections.Users, new[]
            {
                Row("u1", ("active", OrgInfoTokens.True)),
                Row("u2", ("active", OrgInfoTokens.True)),
                Row("u3", ("active", OrgInfoTokens.False)),
            }),
            Section(OrgInfoSections.Profiles, new[] { Row("p1"), Row("p2") }),
            Section(OrgInfoSections.PermissionSets, new[] { Row("ps1") }),
            Section(OrgInfoSections.Roles, new[] { Row("r1") }),
            Section(OrgInfoSections.Objects, new[]
            {
                Row("Account", ("kind", OrgInfoTokens.Standard)),
                Row("My_Obj__c", ("kind", OrgInfoTokens.Custom)),
                Row("My_Obj2__c", ("kind", OrgInfoTokens.Custom)),
            }));

        Assert.Equal("3", OrgInfoCatalog.ComputeStat("totalUsers", provider));
        Assert.Equal("2", OrgInfoCatalog.ComputeStat("activeUsers", provider));
        Assert.Equal("2", OrgInfoCatalog.ComputeStat("profiles", provider));
        Assert.Equal("1", OrgInfoCatalog.ComputeStat("permissionSets", provider));
        Assert.Equal("1", OrgInfoCatalog.ComputeStat("roles", provider));
        Assert.Equal("3", OrgInfoCatalog.ComputeStat("objects", provider));
        Assert.Equal("2", OrgInfoCatalog.ComputeStat("customObjects", provider));
        Assert.Null(OrgInfoCatalog.ComputeStat("totalUsers", _ => null));
        Assert.Null(OrgInfoCatalog.ComputeStat("nope", provider));
    }
}
