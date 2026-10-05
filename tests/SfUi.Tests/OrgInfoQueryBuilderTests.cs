using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgInfoQueryBuilderTests
{
    [Fact]
    public void OrganizationQuery_ContainsOverviewFields()
    {
        var query = OrgInfoQueryBuilder.BuildOrganizationQuery();

        Assert.StartsWith("SELECT Id, Name", query);
        Assert.Contains("OrganizationType", query);
        Assert.Contains("InstanceName", query);
        Assert.Contains("IsSandbox", query);
        Assert.Contains("LanguageLocaleKey", query);
        Assert.Contains("TimeZoneSidKey", query);
        Assert.Contains("FiscalYearStartMonth", query);
        Assert.EndsWith("FROM Organization", query);
    }

    [Fact]
    public void OrganizationOwdsQuery_ContainsDefaultAccessFields()
    {
        var query = OrgInfoQueryBuilder.BuildOrganizationOwdsQuery();

        Assert.Contains("DefaultAccountAccess", query);
        Assert.Contains("DefaultContactAccess", query);
        Assert.Contains("DefaultOpportunityAccess", query);
        Assert.Contains("DefaultLeadAccess", query);
        Assert.Contains("DefaultCaseAccess", query);
        Assert.Contains("DefaultPricebookAccess", query);
        Assert.Contains("DefaultCalendarAccess", query);
        Assert.Contains("DefaultCampaignAccess", query);
    }

    [Fact]
    public void UserQuery_SelectsRelationshipNamesAndLoginDate()
    {
        var query = OrgInfoQueryBuilder.BuildUserQuery();

        Assert.Contains("Profile.Name", query);
        Assert.Contains("UserRole.Name", query);
        Assert.Contains("LastLoginDate", query);
        Assert.Contains("FROM User", query);
    }

    [Fact]
    public void ProfileCountQuery_GroupsByProfile()
    {
        var query = OrgInfoQueryBuilder.BuildProfileUserCountQuery();

        Assert.Contains("COUNT(Id)", query);
        Assert.Contains("GROUP BY ProfileId", query);
        Assert.Contains("IsActive = true", query);
    }

    [Fact]
    public void PermissionSetQuery_ExcludesProfileOwnedSets()
    {
        var query = OrgInfoQueryBuilder.BuildPermissionSetQuery();

        Assert.Contains("FROM PermissionSet", query);
        Assert.Contains("IsOwnedByProfile = false", query);
    }

    [Fact]
    public void PermissionSetAssignmentCountQuery_GroupsByPermissionSet()
    {
        var query = OrgInfoQueryBuilder.BuildPermissionSetAssignmentCountQuery();

        Assert.Contains("COUNT(Id)", query);
        Assert.Contains("GROUP BY PermissionSetId", query);
    }

    [Fact]
    public void RoleQuery_SelectsParentRole()
    {
        var query = OrgInfoQueryBuilder.BuildUserRoleQuery();

        Assert.Contains("ParentRoleId", query);
        Assert.Contains("FROM UserRole", query);
    }

    [Fact]
    public void EntityDefinitionQuery_SelectsSharingModelsAndFilters()
    {
        var query = OrgInfoQueryBuilder.BuildEntityDefinitionQuery();

        Assert.Contains("InternalSharingModel", query);
        Assert.Contains("ExternalSharingModel", query);
        Assert.Contains("IsCustomizable = true", query);
        Assert.Contains("IsDeprecatedAndHidden = false", query);
        Assert.Contains("FROM EntityDefinition", query);
    }

    [Fact]
    public void FieldDefinitionQuery_FiltersByEntity()
    {
        var query = OrgInfoQueryBuilder.BuildFieldDefinitionQuery("Account");

        Assert.Contains("FROM FieldDefinition", query);
        Assert.Contains("WHERE EntityDefinition.QualifiedApiName = 'Account'", query);
        Assert.Contains("ReferenceTo", query);
        Assert.Contains("IsFieldHistoryTracked", query);
    }

    [Fact]
    public void FieldDefinitionQuery_EscapesQuotes()
    {
        var query = OrgInfoQueryBuilder.BuildFieldDefinitionQuery("O'Brien__c");

        Assert.Contains("'O\\'Brien__c'", query);
        Assert.Equal("O\\'Brien", OrgInfoQueryBuilder.EscapeSoqlString("O'Brien"));
        Assert.Equal("a\\\\b", OrgInfoQueryBuilder.EscapeSoqlString("a\\b"));
    }

    // ---- Step 4: 主な設定・追加候補 ----

    [Fact]
    public void SettingsQuery_ContainsPreferenceFields()
    {
        var query = OrgInfoQueryBuilder.BuildSettingsQuery();

        Assert.Contains("PreferencesLightningLoginEnabled", query);
        Assert.Contains("PreferencesOnlyLLPermUserAllowed", query);
        Assert.Contains("ComplianceBccEmail", query);
        Assert.Contains("UiSkin", query);
        Assert.Contains("TrialExpirationDate", query);
        Assert.Contains("IsReadOnly", query);
        Assert.EndsWith("FROM Organization", query);
    }

    [Fact]
    public void SettingsQuery_ExcludesFieldsMissingFromTheOrg()
    {
        // hks4 のように Transaction Security 未導入の組織では PreferencesTransactionSecurityPolicy が存在しない
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Id",
            "PreferencesLightningLoginEnabled",
            "PreferencesOnlyLLPermUserAllowed",
            "PreferencesConsentManagementEnabled",
            "UiSkin",
            "IsReadOnly",
        };

        var query = OrgInfoQueryBuilder.BuildSettingsQuery(existing);

        Assert.Contains("PreferencesLightningLoginEnabled", query);
        Assert.Contains("PreferencesConsentManagementEnabled", query);
        Assert.Contains("UiSkin", query);
        Assert.Contains("IsReadOnly", query);
        Assert.DoesNotContain("PreferencesTransactionSecurityPolicy", query);
        Assert.EndsWith("FROM Organization", query);
    }

    [Fact]
    public void ApexQueries_SelectStatusAndValid()
    {
        var classes = OrgInfoQueryBuilder.BuildApexClassQuery();
        Assert.Contains("ApiVersion", classes);
        Assert.Contains("Status", classes);
        Assert.Contains("IsValid", classes);
        Assert.Contains("LengthWithoutComments", classes);
        Assert.Contains("FROM ApexClass", classes);

        var triggers = OrgInfoQueryBuilder.BuildApexTriggerQuery();
        Assert.Contains("FROM ApexTrigger", triggers);
        Assert.Contains("IsValid", triggers);
    }

    [Fact]
    public void FlowQuery_UsesDefinitionView()
    {
        var query = OrgInfoQueryBuilder.BuildFlowQuery();

        Assert.Contains("ProcessType", query);
        Assert.Contains("IsActive", query);
        Assert.Contains("FROM FlowDefinitionView", query);
    }

    [Fact]
    public void ScheduledJobQuery_FiltersDeletedAndOrdersByNextFire()
    {
        var query = OrgInfoQueryBuilder.BuildScheduledJobQuery();

        Assert.Contains("CronJobDetail.Name", query);
        Assert.Contains("CronJobDetail.JobType", query);
        Assert.Contains("State != 'DELETED'", query);
        Assert.Contains("ORDER BY NextFireTime", query);
        Assert.Contains("FROM CronTrigger", query);
    }

    [Fact]
    public void ConnectedAppAndPackageQueries_SelectExpectedFields()
    {
        Assert.Contains("FROM ConnectedApplication", OrgInfoQueryBuilder.BuildConnectedAppQuery());

        var packages = OrgInfoQueryBuilder.BuildInstalledPackageQuery();
        Assert.Contains("SubscriberPackage.Name", packages);
        Assert.Contains("SubscriberPackage.NamespacePrefix", packages);
        Assert.Contains("FROM InstalledSubscriberPackage", packages);

        var version = OrgInfoQueryBuilder.BuildPackageVersionQuery("04t000000000001AAA");
        Assert.Contains("WHERE Id = '04t000000000001AAA'", version);
        Assert.Contains("MajorVersion", version);
    }

    [Fact]
    public void AuditQueries_LimitToRecentRows()
    {
        var login = OrgInfoQueryBuilder.BuildLoginHistoryQuery();
        Assert.Contains("FROM LoginHistory", login);
        Assert.Contains("ORDER BY LoginTime DESC LIMIT 200", login);

        var audit = OrgInfoQueryBuilder.BuildAuditTrailQuery();
        Assert.Contains("CreatedBy.Name", audit);
        Assert.Contains("FROM SetupAuditTrail", audit);
        Assert.Contains("ORDER BY CreatedDate DESC LIMIT 200", audit);
    }

    [Fact]
    public void RecordTypeAndCurrencyQueries_SelectExpectedFields()
    {
        var recordTypes = OrgInfoQueryBuilder.BuildRecordTypeQuery();
        Assert.Contains("SobjectType", recordTypes);
        Assert.Contains("IsActive = true", recordTypes);

        var currencies = OrgInfoQueryBuilder.BuildCurrencyQuery();
        Assert.Contains("IsoCode", currencies);
        Assert.Contains("ConversionRate", currencies);
        Assert.Contains("FROM CurrencyType", currencies);
    }
}
