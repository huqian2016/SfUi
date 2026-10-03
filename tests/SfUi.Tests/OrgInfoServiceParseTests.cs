using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgInfoServiceParseTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static OrgInfo DemoOrg() => new(
        Username: "user@example.com",
        Alias: "demo",
        OrgId: "00D000000000000AAA",
        InstanceUrl: "https://demo.my.salesforce.com",
        ConnectedStatus: "Connected",
        IsDefault: true,
        IsSandbox: true);

    private static OrgAuthInfo Auth() => new(
        TargetOrg: "demo",
        Username: "user@example.com",
        InstanceUrl: "https://demo.my.salesforce.com",
        AccessToken: "dummy-token",
        ApiVersion: "67.0",
        FetchedAt: DateTimeOffset.Now);

    [Fact]
    public void ParseOverviewRows_MapsOrganizationAndLimits()
    {
        var organization = Json("""
        {
          "Id": "00D000000000000AAA",
          "Name": "HKS Demo",
          "OrganizationType": "Enterprise Edition",
          "InstanceName": "AP99",
          "IsSandbox": true,
          "LanguageLocaleKey": "ja",
          "DefaultLocaleSidKey": "ja_JP",
          "TimeZoneSidKey": "Asia/Tokyo",
          "FiscalYearStartMonth": 4,
          "Phone": "03-0000-0000",
          "Street": "1-2-3 Marunouchi",
          "City": "Chiyoda",
          "State": "Tokyo",
          "PostalCode": "100-0001",
          "Country": "Japan"
        }
        """);
        var limits = Json("""
        {
          "DataStorageMB": { "Max": 1024, "Remaining": 1000 },
          "FileStorageMB": { "Max": 1024, "Remaining": 1024 },
          "DailyApiRequests": { "Max": 15000, "Remaining": 14900 }
        }
        """);

        var rows = OrgInfoService.ParseOverviewRows(DemoOrg(), Auth(), organization, limits);

        Assert.Equal(OrgInfoSections.OverviewItems.Count, rows.Count);
        var byId = rows.ToDictionary(r => r.Id);
        Assert.Equal("HKS Demo", byId["orgName"].Get("value"));
        Assert.Equal("00D000000000000AAA", byId["orgId"].Get("value"));
        Assert.Equal("Enterprise Edition", byId["edition"].Get("value"));
        Assert.Equal(OrgInfoTokens.Sandbox, byId["environment"].Get("value"));
        Assert.Equal("67.0", byId["apiVersion"].Get("value"));
        Assert.Equal("4", byId["fiscalYearStart"].Get("value"));
        Assert.Contains("Chiyoda", byId["address"].Get("value"));
        Assert.Equal("1000 / 1024 MB", byId["dataStorage"].Get("value"));
        Assert.Equal("14900 / 15000", byId["dailyApiRequests"].Get("value"));

        // item ラベルは言語依存のためキャッシュに書かない（表示時に OverviewItems から解決）
        Assert.Null(byId["orgName"].Get("item"));
        Assert.Equal("HKS Demo", byId["orgName"].Summary);
    }

    [Fact]
    public void ParseUserRows_MapsNestedProfileAndRole()
    {
        var records = new[]
        {
            Json("""
            {
              "Id": "005xx",
              "Name": "Taro Yamada",
              "Username": "taro@example.com",
              "Email": "taro@example.com",
              "IsActive": true,
              "UserType": "Standard",
              "LastLoginDate": "2026-09-30T12:34:56.000+0000",
              "CreatedDate": "2024-01-01T00:00:00.000+0000",
              "Profile": { "Name": "System Administrator" },
              "UserRole": { "Name": "CEO" }
            }
            """),
        };

        var row = Assert.Single(OrgInfoService.ParseUserRows(records, "https://demo.my.salesforce.com"));

        Assert.Equal("Taro Yamada", row.Summary);
        Assert.Equal(OrgInfoTokens.True, row.Get("active"));
        Assert.Equal("System Administrator", row.Get("profile"));
        Assert.Equal("CEO", row.Get("role"));
        Assert.Equal("2026-09-30T12:34:56.000+0000", row.Get("lastLogin")); // 表示用の整形は UI 側
        Assert.Equal("https://demo.my.salesforce.com/lightning/setup/ManageUsers/page?address=/005xx", row.Link);
    }

    [Fact]
    public void ParseUserRows_WithoutInstanceUrlOrRole_HasNoLinkAndNullCells()
    {
        var records = new[] { Json("""{ "Id": "005xx", "Name": "Taro", "IsActive": false }""") };

        var row = Assert.Single(OrgInfoService.ParseUserRows(records, instanceUrl: null));

        Assert.Null(row.Link);
        Assert.Equal(OrgInfoTokens.False, row.Get("active"));
        Assert.Null(row.Get("profile"));
        Assert.Null(row.Get("role"));
    }

    [Fact]
    public void ParseProfileUserCounts_ReadsAliasAndExprFallback()
    {
        var counts = OrgInfoService.ParseProfileUserCounts(new[]
        {
            Json("""{ "ProfileId": "00e1", "cnt": 3 }"""),
            Json("""{ "ProfileId": "00e2", "expr0": 5 }"""),
        });

        Assert.Equal(3, counts["00e1"]);
        Assert.Equal(5, counts["00e2"]);
    }

    [Fact]
    public void ParseProfileRows_MergesActiveUserCountAndLink()
    {
        var rows = OrgInfoService.ParseProfileRows(
            new[]
            {
                Json("""{ "Id": "00e1", "Name": "System Administrator", "UserType": "Standard", "CreatedDate": "2024-01-01" }"""),
                Json("""{ "Id": "00e2", "Name": "Standard User" }"""),
            },
            new Dictionary<string, int> { ["00e1"] = 3 },
            "https://demo.my.salesforce.com");

        Assert.Equal("3", rows[0].Get("activeUsers"));
        Assert.Null(rows[1].Get("activeUsers")); // 集計に無い場合は null
        Assert.Equal("https://demo.my.salesforce.com/lightning/setup/Profiles/page?address=/00e1", rows[0].Link);
    }

    [Fact]
    public void ParsePermissionSetRows_MergesAssignmentCount()
    {
        var rows = OrgInfoService.ParsePermissionSetRows(
            new[]
            {
                Json("""{ "Id": "0PS1", "Name": "Sales_User", "Label": "Sales User", "Description": "Sales", "CreatedDate": "2024-01-01" }"""),
            },
            new Dictionary<string, int> { ["0PS1"] = 2 });

        var row = Assert.Single(rows);

        Assert.Equal("Sales User", row.Summary);
        Assert.Equal("Sales_User", row.Get("apiName"));
        Assert.Equal("2", row.Get("assignedUsers"));
    }

    [Fact]
    public void ParsePermissionSetAssignmentCounts_ReadsRecords()
    {
        var counts = OrgInfoService.ParsePermissionSetAssignmentCounts(new[]
        {
            Json("""{ "PermissionSetId": "0PS1", "cnt": 2 }"""),
        });

        Assert.Equal(2, counts["0PS1"]);
    }

    [Fact]
    public void ParseRoleRows_ResolvesParentRoleName()
    {
        var rows = OrgInfoService.ParseRoleRows(new[]
        {
            Json("""{ "Id": "00E1", "Name": "CEO", "DeveloperName": "CEO" }"""),
            Json("""{ "Id": "00E2", "Name": "Sales VP", "DeveloperName": "Sales_VP", "ParentRoleId": "00E1" }"""),
            Json("""{ "Id": "00E3", "Name": "Unknown Parent", "ParentRoleId": "00X9" }"""),
        });

        Assert.Equal("CEO", rows.Single(r => r.Id == "00E2").Get("parentRole"));
        Assert.Equal("00X9", rows.Single(r => r.Id == "00E3").Get("parentRole")); // 一覧に無い場合は ID のまま
        Assert.Equal("Sales VP", rows.Single(r => r.Id == "00E2").Summary);
    }

    [Fact]
    public void ParseObjectRows_ClassifiesStandardAndCustom()
    {
        var rows = OrgInfoService.ParseObjectRows(
            new[]
            {
                Json("""{ "QualifiedApiName": "Account", "Label": "Account", "KeyPrefix": "001", "IsCustomizable": true, "IsCustomSetting": false, "InternalSharingModel": "Read", "ExternalSharingModel": "Private" }"""),
                Json("""{ "QualifiedApiName": "My_Object__c", "Label": "My Object", "KeyPrefix": "a01", "IsCustomizable": true, "IsCustomSetting": false, "InternalSharingModel": "Edit", "ExternalSharingModel": "None", "NamespacePrefix": "pkg" }"""),
                Json("""{ "QualifiedApiName": "My_Setting__c", "Label": "My Setting", "IsCustomizable": true, "IsCustomSetting": true }"""),
            },
            "https://demo.my.salesforce.com");

        Assert.Equal(OrgInfoTokens.Standard, rows[0].Get("kind"));
        Assert.Equal(OrgInfoTokens.Custom, rows[1].Get("kind"));
        Assert.Equal("pkg", rows[1].Get("namespace"));
        Assert.Equal(OrgInfoTokens.True, rows[2].Get("customSetting"));
        Assert.Equal("https://demo.my.salesforce.com/lightning/setup/ObjectManager/Account/Details/view", rows[0].Link);
    }

    [Fact]
    public void ParseOwdRows_CombinesOrgDefaultsAndObjectModels()
    {
        var organization = Json("""
        {
          "DefaultAccountAccess": "Read",
          "DefaultContactAccess": "ControlledByParent",
          "DefaultOpportunityAccess": "None",
          "DefaultLeadAccess": "ReadEditTransfer",
          "DefaultCaseAccess": "None",
          "DefaultPricebookAccess": "ReadSelect",
          "DefaultCalendarAccess": "HideDetails",
          "DefaultCampaignAccess": "None"
        }
        """);
        var objects = new[]
        {
            Json("""{ "QualifiedApiName": "Account", "InternalSharingModel": "Read", "ExternalSharingModel": "Private" }"""),
        };

        var rows = OrgInfoService.ParseOwdRows(organization, objects);

        Assert.Equal(OrgInfoSections.OwdDefaultTargets.Count + 1, rows.Count);

        var accountDefault = rows.Single(r => r.Id == "org:Account");
        Assert.Equal("Read", accountDefault.Get("internal"));
        Assert.Equal(OrgInfoTokens.OrgDefault, accountDefault.Get("source"));

        var accountObject = rows.Single(r => r.Id == "Account");
        Assert.Equal(OrgInfoTokens.Object, accountObject.Get("source"));
        Assert.Equal("Private", accountObject.Get("external"));
    }

    [Fact]
    public void ParseOwdRows_WithoutOrganizationRecord_SkipsDefaults()
    {
        var rows = OrgInfoService.ParseOwdRows(default, Array.Empty<JsonElement>());

        Assert.Empty(rows);
    }

    [Fact]
    public void ParseFieldRows_MapsFlagsAndReferenceTo()
    {
        var records = new[]
        {
            Json("""{ "QualifiedApiName": "AccountId", "Label": "Account ID", "DataType": "Lookup(Account)", "ReferenceTo": { "referenceTo": ["Account"] }, "IsNillable": true, "IsIndexed": true, "IsCalculated": false, "IsFieldHistoryTracked": false, "Description": "Lookup" }"""),
            Json("""{ "QualifiedApiName": "MyFormula__c", "Label": "My Formula", "DataType": "Formula(Text)", "IsNillable": false, "IsCalculated": true }"""),
        };

        var rows = OrgInfoService.ParseFieldRows(records);

        Assert.Equal("AccountId", rows[0].Id);
        Assert.Equal(OrgInfoTokens.False, rows[0].Get("custom"));
        Assert.Equal("Account", rows[0].Get("referenceTo"));
        Assert.Equal(OrgInfoTokens.True, rows[0].Get("indexed"));
        Assert.Equal("Lookup", rows[0].Get("description"));
        Assert.Equal(OrgInfoTokens.True, rows[1].Get("custom"));
        Assert.Equal(OrgInfoTokens.True, rows[1].Get("calculated"));
        Assert.Null(rows[1].Get("referenceTo"));
    }

    // ---- Step 4: 主な設定・追加候補セクションのパース ----

    [Fact]
    public void ParseSettingsRows_MapsValuesLinksAndLinkOnly()
    {
        var organization = Json("""
        {
          "PreferencesLightningLoginEnabled": true,
          "PreferencesOnlyLLPermUserAllowed": false,
          "ComplianceBccEmail": "bcc@example.com",
          "UiSkin": "Theme3",
          "IsReadOnly": false
        }
        """);

        var rows = OrgInfoService.ParseSettingsRows(organization, "https://demo.my.salesforce.com");

        Assert.Equal(OrgInfoSections.SettingsItems.Count, rows.Count);

        var lightningLogin = rows.Single(r => r.Id == "lightningLogin");
        Assert.Equal(OrgInfoTokens.True, lightningLogin.Get("value"));
        Assert.Null(lightningLogin.Link);

        var bcc = rows.Single(r => r.Id == "complianceBccEmail");
        Assert.Equal("bcc@example.com", bcc.Get("value"));

        var session = rows.Single(r => r.Id == "sessionSettings");
        Assert.Equal(OrgInfoTokens.LinkOnly, session.Get("value"));
        Assert.Equal("https://demo.my.salesforce.com/lightning/setup/SessionSettings/home", session.Link);

        var audit = rows.Single(r => r.Id == "auditTrail");
        Assert.Equal("https://demo.my.salesforce.com/lightning/setup/SecurityAuditTrail/home", audit.Link);
    }

    [Fact]
    public void ParseSettingsRows_WithoutInstanceUrl_HasNoLinks()
    {
        var rows = OrgInfoService.ParseSettingsRows(Json("""{ "IsReadOnly": true }"""), null);

        var session = rows.Single(r => r.Id == "sessionSettings");
        Assert.Equal(OrgInfoTokens.LinkOnly, session.Get("value"));
        Assert.Null(session.Link);

        var readOnly = rows.Single(r => r.Id == "isReadOnly");
        Assert.Equal(OrgInfoTokens.True, readOnly.Get("value"));
    }

    [Fact]
    public void ParseApexClassRows_MapsFields()
    {
        var records = new[]
        {
            Json("""{ "Id": "01p1", "Name": "MyClass", "ApiVersion": 61, "Status": "Active", "IsValid": true, "LengthWithoutComments": 1234, "LastModifiedDate": "2026-09-01T00:00:00.000+0000" }"""),
        };

        var rows = OrgInfoService.ParseApexClassRows(records);

        Assert.Equal("01p1", rows[0].Id);
        Assert.Equal("MyClass", rows[0].Summary);
        Assert.Equal("61", rows[0].Get("apiVersion"));
        Assert.Equal("Active", rows[0].Get("status"));
        Assert.Equal(OrgInfoTokens.True, rows[0].Get("valid"));
        Assert.Equal("1234", rows[0].Get("length"));
    }

    [Fact]
    public void ParseApexTriggerRows_MapsFields()
    {
        var records = new[]
        {
            Json("""{ "Id": "01q1", "Name": "MyTrigger", "ApiVersion": 62, "Status": "Active", "IsValid": false, "LastModifiedDate": "2026-09-02T00:00:00.000+0000" }"""),
        };

        var rows = OrgInfoService.ParseApexTriggerRows(records);

        Assert.Equal("MyTrigger", rows[0].Summary);
        Assert.Equal(OrgInfoTokens.False, rows[0].Get("valid"));
        Assert.Equal("62", rows[0].Get("apiVersion"));
    }

    [Fact]
    public void ParseFlowRows_MapsProcessTypeAndActive()
    {
        var records = new[]
        {
            Json("""{ "Id": "3001", "Label": "Approve a Deal", "ApiName": "DealApproval", "ProcessType": "AutoLaunchedFlow", "IsActive": true, "LastModifiedDate": "2026-05-30T00:00:00.000+0000" }"""),
        };

        var rows = OrgInfoService.ParseFlowRows(records);

        Assert.Equal("Approve a Deal", rows[0].Summary);
        Assert.Equal("AutoLaunchedFlow", rows[0].Get("processType"));
        Assert.Equal(OrgInfoTokens.True, rows[0].Get("active"));
        Assert.Equal("DealApproval", rows[0].Get("apiName"));
    }

    [Fact]
    public void ParseScheduledJobRows_ReadsNestedCronJobDetail()
    {
        var records = new[]
        {
            Json("""{ "Id": "08e1", "CronJobDetail": { "Name": "Daily Sync", "JobType": "7" }, "NextFireTime": "2026-10-04T00:00:00.000+0000", "State": "WAITING", "TimesTriggered": 3, "CreatedDate": "2026-01-01T00:00:00.000+0000" }"""),
        };

        var rows = OrgInfoService.ParseScheduledJobRows(records);

        Assert.Equal("Daily Sync", rows[0].Summary);
        Assert.Equal("7", rows[0].Get("jobType"));
        Assert.Equal("WAITING", rows[0].Get("state"));
        Assert.Equal("3", rows[0].Get("timesTriggered"));
    }

    [Fact]
    public void ParseConnectedAppRows_MapsFields()
    {
        var records = new[]
        {
            Json("""{ "Id": "0H41", "Name": "Salesforce CLI", "CreatedDate": "2024-12-21T13:33:58.000+0000", "LastModifiedDate": "2025-01-01T00:00:00.000+0000" }"""),
        };

        var rows = OrgInfoService.ParseConnectedAppRows(records);

        Assert.Equal("Salesforce CLI", rows[0].Summary);
        Assert.Equal("2024-12-21T13:33:58.000+0000", rows[0].Get("created"));
    }

    [Fact]
    public void ParseInstalledPackageRows_UsesVersionMapAndFallback()
    {
        var records = new[]
        {
            Json("""{ "Id": "0A31", "SubscriberPackageVersionId": "04t1", "SubscriberPackage": { "Name": "Salesforce Connected Apps", "NamespacePrefix": "sf_com_apps" } }"""),
            Json("""{ "Id": "0A32", "SubscriberPackageVersionId": "04t2", "SubscriberPackage": { "Name": "Other", "NamespacePrefix": "other" } }"""),
        };
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["04t1"] = "1.2" };

        var rows = OrgInfoService.ParseInstalledPackageRows(records, versions);

        Assert.Equal("Salesforce Connected Apps", rows[0].Summary);
        Assert.Equal("sf_com_apps", rows[0].Get("namespace"));
        Assert.Equal("1.2", rows[0].Get("version"));
        Assert.Equal("04t2", rows[1].Get("version"));
    }

    [Fact]
    public void ParseLoginHistoryRows_ResolvesUserNameAndFallsBackToUserId()
    {
        var records = new[]
        {
            Json("""{ "Id": "0Ya1", "UserId": "0051", "LoginTime": "2026-10-03T06:35:20.000+0000", "LoginType": "Remote Access 2.0", "Status": "Success", "SourceIp": "203.0.113.1", "Browser": "Chrome", "Platform": "Windows" }"""),
            Json("""{ "Id": "0Ya2", "UserId": "0052", "LoginTime": "2026-10-03T05:00:00.000+0000", "Status": "Failed" }"""),
        };
        var userNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["0051"] = "Taro Yamada" };

        var rows = OrgInfoService.ParseLoginHistoryRows(records, userNames);

        Assert.Equal("Taro Yamada", rows[0].Get("user"));
        Assert.Equal("Success", rows[0].Get("status"));
        Assert.Equal("Chrome", rows[0].Get("browser"));
        Assert.Equal("0052", rows[1].Get("user"));
    }

    [Fact]
    public void ParseAuditTrailRows_ToleratesNullCreatedByAndSection()
    {
        var records = new[]
        {
            Json("""{ "Id": "0Ym1", "Action": "value_MAX_STREAMING_TOPICS_PROV", "Section": null, "CreatedDate": "2026-09-13T13:24:01.000+0000", "CreatedBy": null, "Display": "Max number of streaming topics" }"""),
            Json("""{ "Id": "0Ym2", "Action": "PermSetCreate", "Section": "Manage Users", "CreatedDate": "2026-09-14T00:00:00.000+0000", "CreatedBy": { "Name": "Admin User" }, "Display": "Created permission set" }"""),
        };

        var rows = OrgInfoService.ParseAuditTrailRows(records);

        Assert.Null(rows[0].Get("user"));
        Assert.Equal("value_MAX_STREAMING_TOPICS_PROV", rows[0].Get("action"));
        Assert.Equal("Admin User", rows[1].Get("user"));
        Assert.Equal("Manage Users", rows[1].Get("section"));
    }

    [Fact]
    public void ParseRecordTypeRows_MapsFields()
    {
        var records = new[]
        {
            Json("""{ "Id": "0121", "Name": "B2B", "DeveloperName": "B2B", "SobjectType": "Account", "IsActive": true, "Description": "Business", "CreatedDate": "2026-01-01T00:00:00.000+0000" }"""),
        };

        var rows = OrgInfoService.ParseRecordTypeRows(records);

        Assert.Equal("B2B", rows[0].Summary);
        Assert.Equal("Account", rows[0].Get("sobject"));
        Assert.Equal("Business", rows[0].Get("description"));
    }

    [Fact]
    public void ParseCurrencyRows_MapsFields()
    {
        var records = new[]
        {
            Json("""{ "Id": "1", "IsoCode": "JPY", "Name": "Japanese Yen", "IsActive": true, "ConversionRate": 1.0, "CreatedDate": "2026-01-01T00:00:00.000+0000" }"""),
        };

        var rows = OrgInfoService.ParseCurrencyRows(records);

        Assert.Equal("JPY", rows[0].Summary);
        Assert.Equal(OrgInfoTokens.True, rows[0].Get("active"));
        Assert.Equal("1", rows[0].Get("conversionRate"));
    }

    [Fact]
    public void FormatPackageVersion_FormatsMajorMinorPatchBuild()
    {
        Assert.Equal("1.2", OrgInfoService.FormatPackageVersion(Json("""{ "MajorVersion": 1, "MinorVersion": 2, "PatchVersion": 0, "BuildNumber": 0 }""")));
        Assert.Equal("1.2.3", OrgInfoService.FormatPackageVersion(Json("""{ "MajorVersion": 1, "MinorVersion": 2, "PatchVersion": 3 }""")));
        Assert.Equal("1.2.3 (build 4)", OrgInfoService.FormatPackageVersion(Json("""{ "MajorVersion": 1, "MinorVersion": 2, "PatchVersion": 3, "BuildNumber": 4 }""")));
        Assert.Null(OrgInfoService.FormatPackageVersion(Json("""{ }""")));
    }

    [Fact]
    public void FetchSectionAsync_UnknownSection_Throws()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            var paths = AppPaths.Resolve(dataRootOverride: sandbox, baseDirectory: sandbox, appDataDirectory: sandbox);
            var log = new AppLog(paths);
            var orgs = new OrgService(new SfCliRunner());
            var service = new OrgInfoService(new SalesforceRestClient(orgs, log), orgs, new OrgInfoCacheStore(paths, log), log);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                _ = service.FetchSectionAsync(DemoOrg(), "unknown");
            });
        }
        finally
        {
            try
            {
                Directory.Delete(sandbox, recursive: true);
            }
            catch
            {
                // 後始末の失敗はテスト結果に影響させない
            }
        }
    }
}
