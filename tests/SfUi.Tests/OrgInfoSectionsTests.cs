using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgInfoSectionsTests
{
    [Fact]
    public void SectionIds_AreUnique()
    {
        var ids = OrgInfoSections.All.Select(d => d.Id).ToList();

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void DefaultSectionIds_MatchAutoFetchDefinitions()
    {
        var expected = OrgInfoSections.All.Where(d => d.AutoFetch).Select(d => d.Id).ToArray();

        Assert.Equal(expected, OrgInfoSections.DefaultSectionIds);
    }

    [Fact]
    public void CatalogLabelKeys_AreDefinedInBothLanguages()
    {
        var keys = new List<string>();
        keys.AddRange(OrgInfoSections.All.Select(d => d.TitleKey));
        keys.AddRange(OrgInfoSections.All.SelectMany(d => OrgInfoSections.ColumnsFor(d.Id)).Select(c => c.LabelKey));
        keys.AddRange(OrgInfoSections.OverviewItems.Select(i => i.LabelKey));
        keys.AddRange(OrgInfoSections.SettingsItems.Select(i => i.LabelKey));
        keys.AddRange(OrgInfoSections.OwdDefaultTargets.Select(t => t.LabelKey));
        keys.Add(OrgInfoSections.FieldsTitleKey);
        keys.AddRange(OrgInfoSections.FieldsColumns.Select(c => c.LabelKey));

        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            Assert.Contains(key, UiText.EnglishKeys);
            Assert.Contains(key, UiText.JapaneseKeys);
        }
    }

    [Fact]
    public void DefaultSectionIds_IncludeSettingsButNotCandidates()
    {
        Assert.Contains(OrgInfoSections.Settings, OrgInfoSections.DefaultSectionIds);
        Assert.DoesNotContain(OrgInfoSections.ApexClasses, OrgInfoSections.DefaultSectionIds);
        Assert.DoesNotContain(OrgInfoSections.LoginHistory, OrgInfoSections.DefaultSectionIds);
    }

    [Fact]
    public void SettingsItems_HaveLabelKeysAndLinkFlags()
    {
        Assert.Equal(OrgInfoSections.SettingsItems.Count, OrgInfoSections.SettingsItems.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("OrgInfo_Settings_UiSkin", OrgInfoSections.SettingsLabelKey("uiSkin"));
        Assert.Null(OrgInfoSections.SettingsLabelKey("nope"));
        Assert.All(OrgInfoSections.SettingsItems.Where(i => !i.IsLink), i => Assert.NotNull(i.OrganizationField));
        Assert.All(OrgInfoSections.SettingsItems.Where(i => i.IsLink), i => Assert.Null(i.OrganizationField));
    }

    [Fact]
    public void FieldsSectionId_BuildsPrefixedId()
    {
        var id = OrgInfoSections.Fields("Account");

        Assert.Equal("fields:Account", id);
        Assert.True(OrgInfoSections.IsFieldsSection(id));
        Assert.False(OrgInfoSections.IsFieldsSection(OrgInfoSections.Users));
    }

    [Fact]
    public void ColumnsFor_KnownSectionsHaveColumnsAndUnknownAreEmpty()
    {
        Assert.NotEmpty(OrgInfoSections.ColumnsFor(OrgInfoSections.Users));
        Assert.NotEmpty(OrgInfoSections.ColumnsFor(OrgInfoSections.Owds));
        Assert.NotEmpty(OrgInfoSections.ColumnsFor(OrgInfoSections.Fields("Account")));
        Assert.Empty(OrgInfoSections.ColumnsFor("unknown"));
    }

    [Fact]
    public void OverviewAndOwdHelpers_ResolveLabelKeys()
    {
        Assert.Equal("OrgInfo_Item_OrgName", OrgInfoSections.OverviewLabelKey("orgName"));
        Assert.Null(OrgInfoSections.OverviewLabelKey("nope"));
        Assert.Equal("OrgInfo_Target_Account", OrgInfoSections.OwdTargetLabelKey("Account"));
        Assert.Null(OrgInfoSections.OwdTargetLabelKey("nope"));
    }

    [Fact]
    public void Find_ReturnsDefinition()
    {
        Assert.Equal("OrgInfo_Tab_Objects", OrgInfoSections.Find(OrgInfoSections.Objects)!.TitleKey);
        Assert.Null(OrgInfoSections.Find("nope"));
    }
}
