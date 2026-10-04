using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public sealed class BackupOrgUrlsTests
{
    private static OrgInfo Org(string username, string? orgId, string? instanceUrl) =>
        new(username, "alias", orgId, instanceUrl, "Connected", IsDefault: false, IsSandbox: false);

    [Fact]
    public void Resolve_PrefersCurrentOrgMatch()
    {
        var current = Org("me@example.com", "00D1", "https://one.my.salesforce.com");
        var orgs = new[] { current };

        Assert.Equal("https://one.my.salesforce.com", BackupOrgUrls.Resolve(current, "me@example.com", "00D1", orgs));
        Assert.Equal("https://one.my.salesforce.com", BackupOrgUrls.Resolve(current, "other@example.com", "00D1", orgs));
        Assert.Equal("https://one.my.salesforce.com", BackupOrgUrls.Resolve(current, "me@example.com", null, orgs));
    }

    [Fact]
    public void Resolve_FallsBackToOrgList()
    {
        var current = Org("me@example.com", "00D1", "https://one.my.salesforce.com");
        var other = Org("backup@example.com", "00D2", "https://two.my.salesforce.com");
        var orgs = new[] { current, other };

        Assert.Equal("https://two.my.salesforce.com", BackupOrgUrls.Resolve(current, "backup@example.com", "00D2", orgs));
        Assert.Equal("https://two.my.salesforce.com", BackupOrgUrls.Resolve(current, null, "00D2", orgs));
    }

    [Fact]
    public void Resolve_ReturnsNullWhenUnknown()
    {
        var current = Org("me@example.com", "00D1", "https://one.my.salesforce.com");
        Assert.Null(BackupOrgUrls.Resolve(current, "ghost@example.com", "00D9", new[] { current }));
        Assert.Null(BackupOrgUrls.Resolve(null, "ghost@example.com", null, Array.Empty<OrgInfo>()));
    }

    [Fact]
    public void BuildRecordUrl_BuildsLightningUrl()
    {
        Assert.Equal(
            "https://one.my.salesforce.com/lightning/r/Account/001XX/view",
            BackupOrgUrls.BuildRecordUrl("https://one.my.salesforce.com/", "Account", "001XX"));
        Assert.Null(BackupOrgUrls.BuildRecordUrl(null, "Account", "001XX"));
        Assert.Null(BackupOrgUrls.BuildRecordUrl("https://one.my.salesforce.com", "Account", null));
        Assert.Null(BackupOrgUrls.BuildRecordUrl("https://one.my.salesforce.com", null, "001XX"));
    }
}
