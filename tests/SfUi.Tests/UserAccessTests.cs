using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>ユーザー中心の実効アクセス（プロファイル + 権限セット + PSG の和集合）のテスト。</summary>
public class UserAccessTests
{
    private static readonly PermissionCatalog Catalog = new(
        new[]
        {
            new PermissionSubject("ps-profile", PermissionSubjectKind.Profile, "System Administrator", "Admin", false),
            new PermissionSubject("ps-sales", PermissionSubjectKind.PermissionSet, "Sales User", "Sales_User", true),
            new PermissionSubject("psg-team", PermissionSubjectKind.PermissionSetGroup, "Team Group", "Team_Group", true),
        },
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["psg-team"] = new[] { "ps-a", "ps-b" },
        });

    private static readonly AccessUser Alice = new("u-alice", "Alice", "alice@example.com", "ps-profile", "System Administrator");

    [Fact]
    public void ResolveSubjectIds_IncludesProfileAssignedSetsAndGroups()
    {
        var assignments = new[]
        {
            new UserPermissionAssignment("u-alice", "ps-sales", null),
            new UserPermissionAssignment("u-alice", "ps-group-member", "psg-team"),
            new UserPermissionAssignment("u-bob", "ps-other", null),
        };

        var ids = EffectiveAccessCalculator.ResolveSubjectIds(Alice, assignments);

        Assert.Contains("ps-profile", ids);
        Assert.Contains("ps-sales", ids);
        Assert.Contains("psg-team", ids);
        Assert.Contains("ps-group-member", ids);
        Assert.DoesNotContain("ps-other", ids);
        Assert.Equal(4, ids.Count);
    }

    [Fact]
    public void ComputeObjectAccess_OrsOnlySelectedSubjects()
    {
        var rows = new[]
        {
            new ObjectAccessRow(Catalog.Subjects[0], Read: true, Create: false, Edit: false, Delete: false, ViewAllRecords: false, ModifyAllRecords: false, ViewAllFields: true),
            new ObjectAccessRow(Catalog.Subjects[1], Read: true, Create: true, Edit: true, Delete: false, ViewAllRecords: true, ModifyAllRecords: false, ViewAllFields: false),
            new ObjectAccessRow(Catalog.Subjects[2], Read: false, Create: false, Edit: false, Delete: true, ViewAllRecords: false, ModifyAllRecords: true, ViewAllFields: false),
        };

        var onlyProfile = EffectiveAccessCalculator.ComputeObjectAccess(rows, new HashSet<string> { "ps-profile" });
        Assert.True(onlyProfile.Read);
        Assert.False(onlyProfile.Edit);
        Assert.True(onlyProfile.ViewAllFields);
        Assert.False(onlyProfile.ModifyAllRecords);

        var profilePlusSales = EffectiveAccessCalculator.ComputeObjectAccess(rows, new HashSet<string> { "ps-profile", "ps-sales" });
        Assert.True(profilePlusSales.Read);
        Assert.True(profilePlusSales.Create);
        Assert.True(profilePlusSales.Edit);
        Assert.True(profilePlusSales.ViewAllRecords);
        Assert.False(profilePlusSales.Delete);
    }

    [Fact]
    public void ComputeFieldAccess_OrsAcrossSubjectsAndTracksGrantSources()
    {
        var fields = new[]
        {
            Field("Name"),
            Field("Industry"),
            Field("AnnualRevenue"),
        };
        var snapshot = Snapshot(
            ("ps-profile", "Account.Name", true, false),
            ("ps-sales", "Account.Name", true, true),
            ("ps-sales", "Account.Industry", true, false));

        var result = EffectiveAccessCalculator.ComputeFieldAccess(
            fields, "Account", snapshot, Catalog, new HashSet<string> { "ps-profile", "ps-sales" });

        var name = result.Single(r => r.FieldApiName == "Name");
        Assert.True(name.Read);
        Assert.True(name.Edit);
        Assert.Equal(new[] { "System Administrator", "Sales User" }, name.ReadGrants.Select(g => g.Label));
        Assert.Equal(new[] { "Sales User" }, name.EditGrants.Select(g => g.Label));

        var industry = result.Single(r => r.FieldApiName == "Industry");
        Assert.True(industry.Read);
        Assert.False(industry.Edit);
        Assert.Empty(industry.EditGrants);

        var revenue = result.Single(r => r.FieldApiName == "AnnualRevenue");
        Assert.False(revenue.Read);
        Assert.False(revenue.Edit);
        Assert.Empty(revenue.ReadGrants);
    }

    [Fact]
    public void ComputeFieldAccess_FollowsCatalogOrderForGrants()
    {
        var fields = new[] { Field("Name") };
        var snapshot = Snapshot(
            ("ps-sales", "Account.Name", true, true),
            ("ps-profile", "Account.Name", true, true));

        // 入力集合の順序に関係なくカタログ順で安定する
        var result = EffectiveAccessCalculator.ComputeFieldAccess(
            fields, "Account", snapshot, Catalog, new HashSet<string> { "ps-sales", "ps-profile" });

        Assert.Equal(
            new[] { "System Administrator", "Sales User" },
            result[0].ReadGrants.Select(g => g.Label));
    }

    [Fact]
    public void Compute_ComposesUserAssignments()
    {
        var objectRows = new[]
        {
            new ObjectAccessRow(Catalog.Subjects[0], true, false, false, false, false, false, false),
            new ObjectAccessRow(Catalog.Subjects[1], true, true, true, true, false, false, false),
        };
        var snapshot = Snapshot(("ps-sales", "Account.Name", true, true));
        var assignments = new[] { new UserPermissionAssignment("u-alice", "ps-sales", null) };

        var access = EffectiveAccessCalculator.Compute(
            Alice, assignments, Catalog, objectRows, snapshot, new[] { Field("Name"), Field("Industry") }, "Account");

        Assert.Equal("u-alice", access.User.Id);
        Assert.True(access.Object.Read);
        Assert.True(access.Object.Edit);
        Assert.True(access.Object.Delete);
        var name = access.Fields.Single(f => f.FieldApiName == "Name");
        Assert.True(name.Edit);
        Assert.Equal("Sales User", Assert.Single(name.EditGrants).Label);
        Assert.False(access.Fields.Single(f => f.FieldApiName == "Industry").Read);
    }

    [Fact]
    public void FormatGrant_DistinguishesProfileSetsAndGroups()
    {
        Assert.Equal("Profile: System Administrator", EffectiveAccessCalculator.FormatGrant(Catalog.Subjects[0]));
        Assert.Equal("PS: Sales User", EffectiveAccessCalculator.FormatGrant(Catalog.Subjects[1]));
        Assert.Equal("PSG: Team Group", EffectiveAccessCalculator.FormatGrant(Catalog.Subjects[2]));
    }

    // ---- 解析 ----

    [Fact]
    public void ParseUsers_ReadsProfileAndSkipsRowsWithoutId()
    {
        using var document = JsonDocument.Parse(
            "{\"records\":[" +
            "{\"Id\":\"005A\",\"Name\":\"Alice\",\"Username\":\"alice@example.com\",\"ProfileId\":\"00e1\",\"Profile\":{\"Name\":\"System Administrator\"}}," +
            "{\"Name\":\"NoId\"}]}");
        var rows = document.RootElement.GetProperty("records").EnumerateArray().ToList();

        var users = UserAccessService.ParseUsers(rows);

        var user = Assert.Single(users);
        Assert.Equal("005A", user.Id);
        Assert.Equal("System Administrator", user.ProfileName);
        Assert.Equal("00e1", user.ProfileId);
        Assert.Equal("Alice (alice@example.com) · System Administrator", user.DisplayWithProfile);
    }

    [Fact]
    public void ParseAssignments_ReadsPermissionSetAndGroup()
    {
        using var document = JsonDocument.Parse(
            "{\"records\":[" +
            "{\"AssigneeId\":\"005A\",\"PermissionSetId\":\"0PS1\",\"PermissionSetGroupId\":null}," +
            "{\"AssigneeId\":\"005A\",\"PermissionSetId\":\"0PS2\",\"PermissionSetGroupId\":\"0PG1\"}," +
            "{\"AssigneeId\":null,\"PermissionSetId\":\"0PS3\"}]}");
        var rows = document.RootElement.GetProperty("records").EnumerateArray().ToList();

        var assignments = UserAccessService.ParseAssignments(rows);

        Assert.Equal(2, assignments.Count);
        Assert.Equal("0PS1", assignments[0].PermissionSetId);
        Assert.Null(assignments[0].PermissionSetGroupId);
        Assert.Equal("0PG1", assignments[1].PermissionSetGroupId);
    }

    // ---- ヘルパー ----

    private static DataIoField Field(string name) =>
        new(name, name, "string", true, true, true, false, false, false, Array.Empty<string>());

    private static FieldAccessSnapshot Snapshot(params (string SubjectId, string Field, bool Read, bool Edit)[] cells)
    {
        var map = new Dictionary<string, IReadOnlyDictionary<string, FieldAccessCell>>(StringComparer.Ordinal);
        foreach (var group in cells.GroupBy(c => c.SubjectId, StringComparer.Ordinal))
        {
            map[group.Key] = group.ToDictionary(
                c => c.Field,
                c => new FieldAccessCell(c.Read, c.Edit),
                StringComparer.OrdinalIgnoreCase);
        }

        return new FieldAccessSnapshot(map);
    }
}
