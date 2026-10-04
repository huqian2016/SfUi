using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public sealed class PermissionAccessServiceTests
{
    private static IReadOnlyList<JsonElement> Rows(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    [Fact]
    public void BuildCatalog_ParsesProfilesPermissionSetsAndGroups()
    {
        var permissionSets = Rows(
            """
            [
              { "Id": "0PSp1", "Name": "X00ex0000", "Label": "X00ex0000", "IsCustom": false, "IsOwnedByProfile": true, "Profile": { "Name": "System Administrator" } },
              { "Id": "0PSs1", "Name": "Sales_Admin", "Label": "営業管理者", "IsCustom": true, "IsOwnedByProfile": false },
              { "Id": "0PSs2", "Name": "Read_Only", "Label": "参照のみ", "IsCustom": true, "IsOwnedByProfile": false, "Profile": null }
            ]
            """);
        var groups = Rows("""[ { "Id": "0PG1", "MasterLabel": "営業セット", "DeveloperName": "Sales_Group" } ]""");
        var components = Rows("""[ { "PermissionSetGroupId": "0PG1", "PermissionSetId": "0PSs1" }, { "PermissionSetGroupId": "0PG1", "PermissionSetId": "0PSs2" } ]""");

        var catalog = PermissionAccessService.BuildCatalog(permissionSets, groups, components);

        Assert.Equal(4, catalog.Subjects.Count);
        // 並び順: プロファイル → 権限セット → PSG
        Assert.Equal(PermissionSubjectKind.Profile, catalog.Subjects[0].Kind);
        Assert.Equal("System Administrator", catalog.Subjects[0].Label);
        Assert.False(catalog.Subjects[0].IsCustom);
        var salesAdmin = catalog.Subjects.Single(s => s.ApiName == "Sales_Admin");
        Assert.Equal(PermissionSubjectKind.PermissionSet, salesAdmin.Kind);
        Assert.Equal("営業管理者", salesAdmin.Label);
        Assert.True(salesAdmin.IsCustom);
        Assert.Equal(PermissionSubjectKind.PermissionSetGroup, catalog.Subjects[3].Kind);
        Assert.Equal("営業セット", catalog.Subjects[3].Label);
        Assert.Equal("Sales_Group", catalog.Subjects[3].ApiName);
        Assert.True(catalog.Subjects[3].IsCustom);

        Assert.Equal(new[] { "0PSs1", "0PSs2" }, catalog.GroupComponents["0PG1"]);
    }

    [Fact]
    public void BuildCatalog_FallsBackToLabel_WhenProfileNameMissing()
    {
        var permissionSets = Rows(
            """[ { "Id": "0PSp1", "Label": "Fallback Label", "IsOwnedByProfile": true } ]""");

        var catalog = PermissionAccessService.BuildCatalog(permissionSets, Array.Empty<JsonElement>(), Array.Empty<JsonElement>());

        Assert.Single(catalog.Subjects);
        Assert.Equal(PermissionSubjectKind.Profile, catalog.Subjects[0].Kind);
        Assert.Equal("Fallback Label", catalog.Subjects[0].Label);
    }

    [Fact]
    public void BuildObjectAccess_UnionsGroupMembers_AndFillsMissingFalse()
    {
        var catalog = PermissionAccessService.BuildCatalog(
            Rows(
                """
                [
                  { "Id": "0PSp1", "Label": "Standard User", "IsOwnedByProfile": true, "Profile": { "Name": "Standard User" } },
                  { "Id": "0PSs1", "Name": "Sales_Admin", "Label": "営業管理者", "IsCustom": true },
                  { "Id": "0PSs2", "Name": "Read_Only", "Label": "参照のみ", "IsCustom": true }
                ]
                """),
            Rows("""[ { "Id": "0PG1", "MasterLabel": "営業セット", "DeveloperName": "Sales_Group" } ]"""),
            Rows("""[ { "PermissionSetGroupId": "0PG1", "PermissionSetId": "0PSs1" }, { "PermissionSetGroupId": "0PG1", "PermissionSetId": "0PSs2" } ]"""));

        var permissionRows = Rows(
            """
            [
              { "ParentId": "0PSs1", "PermissionsRead": true, "PermissionsCreate": true, "PermissionsEdit": true, "PermissionsDelete": false, "PermissionsViewAllRecords": false, "PermissionsModifyAllRecords": false, "PermissionsViewAllFields": true },
              { "ParentId": "0PSs2", "PermissionsRead": true, "PermissionsCreate": false, "PermissionsEdit": false, "PermissionsDelete": false, "PermissionsViewAllRecords": true, "PermissionsModifyAllRecords": false, "PermissionsViewAllFields": false }
            ]
            """);

        var rows = PermissionAccessService.BuildObjectAccess(catalog, permissionRows);

        Assert.Equal(4, rows.Count);
        var profile = rows.Single(r => r.Subject.Id == "0PSp1");
        Assert.False(profile.Read);
        Assert.False(profile.Create);
        Assert.False(profile.ViewAllFields);

        var salesAdmin = rows.Single(r => r.Subject.Id == "0PSs1");
        Assert.True(salesAdmin.Read);
        Assert.True(salesAdmin.Create);
        Assert.True(salesAdmin.ViewAllFields);
        Assert.False(salesAdmin.Delete);

        var group = rows.Single(r => r.Subject.Id == "0PG1");
        Assert.True(group.Read);              // PS1 と PS2 の和集合
        Assert.True(group.Create);            // PS1 のみ
        Assert.True(group.ViewAllRecords);    // PS2 のみ
        Assert.True(group.ViewAllFields);     // PS1 のみ
        Assert.False(group.Delete);
    }

    [Fact]
    public void BuildObjectAccess_GroupWithoutComponents_IsAllFalse()
    {
        var catalog = PermissionAccessService.BuildCatalog(
            Rows("""[ { "Id": "0PSs1", "Name": "A", "Label": "A", "IsCustom": true } ]"""),
            Rows("""[ { "Id": "0PG1", "MasterLabel": "G", "DeveloperName": "G" } ]"""),
            Array.Empty<JsonElement>());

        var rows = PermissionAccessService.BuildObjectAccess(
            catalog,
            Rows("""[ { "ParentId": "0PSs1", "PermissionsRead": true } ]"""));

        var group = rows.Single(r => r.Subject.Id == "0PG1");
        Assert.False(group.Read);
        Assert.False(group.ViewAllFields);
    }

    [Fact]
    public void BuildFieldAccess_UnionsGroupFields_AndKeepsOthers()
    {
        var catalog = PermissionAccessService.BuildCatalog(
            Rows(
                """
                [
                  { "Id": "0PSp1", "Label": "Standard User", "IsOwnedByProfile": true, "Profile": { "Name": "Standard User" } },
                  { "Id": "0PSs1", "Name": "Sales_Admin", "Label": "営業管理者", "IsCustom": true },
                  { "Id": "0PSs2", "Name": "Read_Only", "Label": "参照のみ", "IsCustom": true }
                ]
                """),
            Rows("""[ { "Id": "0PG1", "MasterLabel": "営業セット", "DeveloperName": "Sales_Group" } ]"""),
            Rows("""[ { "PermissionSetGroupId": "0PG1", "PermissionSetId": "0PSs1" }, { "PermissionSetGroupId": "0PG1", "PermissionSetId": "0PSs2" } ]"""));

        var permissionRows = Rows(
            """
            [
              { "ParentId": "0PSs1", "Field": "Account.Name", "PermissionsRead": true, "PermissionsEdit": false },
              { "ParentId": "0PSs2", "Field": "Account.Name", "PermissionsRead": false, "PermissionsEdit": true },
              { "ParentId": "0PSs2", "Field": "Account.BillingCity", "PermissionsRead": true, "PermissionsEdit": false }
            ]
            """);

        var snapshot = PermissionAccessService.BuildFieldAccess(catalog, permissionRows);

        var salesAdmin = snapshot.BySubject["0PSs1"];
        Assert.True(salesAdmin["Account.Name"].Read);
        Assert.False(salesAdmin["Account.Name"].Edit);
        Assert.False(salesAdmin.ContainsKey("Account.BillingCity"));

        var group = snapshot.BySubject["0PG1"];
        Assert.True(group["Account.Name"].Read);
        Assert.True(group["Account.Name"].Edit);
        Assert.True(group["Account.BillingCity"].Read);

        var profile = snapshot.BySubject["0PSp1"];
        Assert.Empty(profile);
    }

    [Theory]
    [InlineData("Account.Name", "Name")]
    [InlineData("Name", "Name")]
    [InlineData("Account.Custom__c", "Custom__c")]
    public void FieldShortName_ReturnsApiName(string field, string expected)
    {
        Assert.Equal(expected, PermissionAccessService.FieldShortName(field));
    }
}
