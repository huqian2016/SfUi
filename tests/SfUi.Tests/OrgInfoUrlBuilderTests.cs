using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgInfoUrlBuilderTests
{
    [Theory]
    [InlineData("https://ap5.my.salesforce.com")]
    [InlineData("https://ap5.my.salesforce.com/")]
    public void SetupHome_NormalizesTrailingSlash(string instanceUrl)
    {
        Assert.Equal(
            "https://ap5.my.salesforce.com/lightning/setup/SetupOneHome/home",
            OrgInfoUrlBuilder.SetupHome(instanceUrl));
    }

    [Fact]
    public void UserDetail_UsesAddressParameter()
    {
        Assert.Equal(
            "https://x.my.salesforce.com/lightning/setup/ManageUsers/page?address=/005xx",
            OrgInfoUrlBuilder.UserDetail("https://x.my.salesforce.com", "005xx"));
    }

    [Fact]
    public void ObjectFields_IncludesObjectApiName()
    {
        Assert.Equal(
            "https://x.my.salesforce.com/lightning/setup/ObjectManager/Account/FieldsAndRelationships/view",
            OrgInfoUrlBuilder.ObjectFields("https://x.my.salesforce.com", "Account"));
    }

    [Fact]
    public void ObjectDetail_IncludesObjectApiName()
    {
        Assert.Equal(
            "https://x.my.salesforce.com/lightning/setup/ObjectManager/My_Object__c/Details/view",
            OrgInfoUrlBuilder.ObjectDetail("https://x.my.salesforce.com/", "My_Object__c"));
    }

    [Fact]
    public void EmptyInstanceUrl_Throws()
    {
        Assert.Throws<ArgumentException>(() => OrgInfoUrlBuilder.SetupHome("   "));
        Assert.Throws<ArgumentException>(() => OrgInfoUrlBuilder.ObjectManager(null!));
    }

    [Fact]
    public void ForSection_MapsKnownSectionsAndHandlesMissingInstanceUrl()
    {
        Assert.Equal(
            "https://x.my.salesforce.com/lightning/setup/ManageUsers/home",
            OrgInfoUrlBuilder.ForSection("https://x.my.salesforce.com", OrgInfoSections.Users));
        Assert.Equal(
            "https://x.my.salesforce.com/lightning/setup/SecuritySharing/home",
            OrgInfoUrlBuilder.ForSection("https://x.my.salesforce.com", OrgInfoSections.Owds));
        Assert.Null(OrgInfoUrlBuilder.ForSection(null, OrgInfoSections.Users));
        Assert.Null(OrgInfoUrlBuilder.ForSection("https://x.my.salesforce.com", OrgInfoSections.Fields("Account")));
    }

    [Fact]
    public void ObjectFieldsOrNull_HandlesMissingInputs()
    {
        Assert.Null(OrgInfoUrlBuilder.ObjectFieldsOrNull(null, "Account"));
        Assert.Null(OrgInfoUrlBuilder.ObjectFieldsOrNull("https://x.my.salesforce.com", ""));
        Assert.Equal(
            "https://x.my.salesforce.com/lightning/setup/ObjectManager/Account/FieldsAndRelationships/view",
            OrgInfoUrlBuilder.ObjectFieldsOrNull("https://x.my.salesforce.com", "Account"));
    }
}
