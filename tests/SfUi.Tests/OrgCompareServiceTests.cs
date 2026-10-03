using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgCompareServiceTests
{
    private static readonly OrgCompareOrgColumn OrgA = new("keyA", "Org A", "a@example.com");
    private static readonly OrgCompareOrgColumn OrgB = new("keyB", "Org B", "b@example.com");
    private static readonly IReadOnlyList<OrgCompareOrgColumn> TwoOrgs = new[] { OrgA, OrgB };

    private static OrgInfoRow Row(string id, string summary, params (string Key, string? Value)[] cells)
    {
        var row = new OrgInfoRow { Id = id, Summary = summary };
        foreach (var (key, value) in cells)
        {
            row.Cells[key] = value;
        }

        return row;
    }

    private static OrgInfoSection Section(string sectionId, params OrgInfoRow[] rows) =>
        OrgInfoSection.Create(sectionId, OrgInfoSections.ColumnsFor(sectionId), rows, DateTimeOffset.Now, 0);

    private static OrgCompareSource Source(
        OrgCompareOrgColumn org,
        string sectionId,
        OrgInfoSection? section,
        OrgCompareCellState state = OrgCompareCellState.Value) =>
        new(org.OrgKey, sectionId, state, section);

    private static OrgCompareCategory Category(string id) => OrgCompareCategories.Find(id)!;

    private static OrgCompareTable Build(string categoryId, params OrgCompareSource[] sources) =>
        OrgCompareService.BuildTable(Category(categoryId), TwoOrgs, sources);

    private static OrgInfoRow UserRow(string id, string username, string profile, string? active = "true") =>
        Row(id, username,
            ("username", username),
            ("name", username),
            ("profile", profile),
            ("role", null),
            ("active", active));

    // ---- Items（概要・設定）----

    [Fact]
    public void Items_SameValues_NoDiff()
    {
        var table = Build(
            OrgInfoSections.Overview,
            Source(OrgA, OrgInfoSections.Overview, Section(OrgInfoSections.Overview, Row("orgName", "A", ("value", "Acme")))),
            Source(OrgB, OrgInfoSections.Overview, Section(OrgInfoSections.Overview, Row("orgName", "A", ("value", "Acme")))));

        Assert.Single(table.Rows);
        Assert.False(table.Rows[0].IsDiff);
        Assert.Equal(0, table.DiffCount);
    }

    [Fact]
    public void Items_ValueDiffers_IsDiff()
    {
        var table = Build(
            OrgInfoSections.Overview,
            Source(OrgA, OrgInfoSections.Overview, Section(OrgInfoSections.Overview, Row("orgName", "A", ("value", "Acme")))),
            Source(OrgB, OrgInfoSections.Overview, Section(OrgInfoSections.Overview, Row("orgName", "B", ("value", "Acme2")))));

        Assert.True(table.Rows[0].IsDiff);
        Assert.Equal(1, table.DiffCount);
    }

    [Fact]
    public void Items_NullAndEmpty_AreEqual()
    {
        var table = Build(
            OrgInfoSections.Overview,
            Source(OrgA, OrgInfoSections.Overview, Section(OrgInfoSections.Overview, Row("orgName", "A", ("value", null)))),
            Source(OrgB, OrgInfoSections.Overview, Section(OrgInfoSections.Overview, Row("orgName", "B", ("value", "")))));

        Assert.False(table.Rows[0].IsDiff);
    }

    [Fact]
    public void Items_RowMissingInOneOrg_IsMissingAndDiff()
    {
        var table = Build(
            OrgInfoSections.Overview,
            Source(OrgA, OrgInfoSections.Overview, Section(OrgInfoSections.Overview, Row("orgName", "A", ("value", "Acme")))),
            Source(OrgB, OrgInfoSections.Overview, Section(OrgInfoSections.Overview, Row("orgId", "B", ("value", "X")))));

        Assert.Equal(OrgCompareCellState.Value, table.Rows[0].Cells[0].State);
        Assert.Equal(OrgCompareCellState.Missing, table.Rows[0].Cells[1].State);
        Assert.True(table.Rows[0].IsDiff);
    }

    [Fact]
    public void Items_NotFetched_IsNotCompared()
    {
        var table = Build(
            OrgInfoSections.Overview,
            Source(OrgA, OrgInfoSections.Overview, Section(OrgInfoSections.Overview, Row("orgName", "A", ("value", "Acme")))),
            Source(OrgB, OrgInfoSections.Overview, null, OrgCompareCellState.NotFetched));

        Assert.Equal(OrgCompareCellState.NotFetched, table.Rows[0].Cells[1].State);
        Assert.False(table.Rows[0].IsDiff);
    }

    [Fact]
    public void Items_NoSections_ReturnsEmptyTable()
    {
        var table = Build(OrgInfoSections.Overview);
        Assert.Empty(table.Rows);
        Assert.Equal(0, table.DiffCount);
    }

    // ---- Keyed（API 名突合）----

    [Fact]
    public void Keyed_UnionOfKeys_MissingMarkedAndDiff()
    {
        var table = Build(
            OrgInfoSections.Users,
            Source(OrgA, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("1", "u1@x.com", "Standard"), UserRow("2", "u2@x.com", "Standard"))),
            Source(OrgB, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("3", "u1@x.com", "Standard"))));

        Assert.Equal(2, table.Rows.Count);
        // ソート順: u1 → u2
        Assert.Equal("u1@x.com", table.Rows[0].Key);
        Assert.False(table.Rows[0].IsDiff);
        Assert.Equal("u2@x.com", table.Rows[1].Key);
        Assert.Equal(OrgCompareCellState.Missing, table.Rows[1].Cells[1].State);
        Assert.True(table.Rows[1].IsDiff);
    }

    [Fact]
    public void Keyed_CaseInsensitiveKeyMatch()
    {
        var table = Build(
            OrgInfoSections.Users,
            Source(OrgA, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("1", "User@X.com", "Standard"))),
            Source(OrgB, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("2", "user@x.com", "Standard"))));

        Assert.Single(table.Rows);
        Assert.All(table.Rows[0].Cells, c => Assert.Equal(OrgCompareCellState.Value, c.State));
        Assert.False(table.Rows[0].IsDiff);
    }

    [Fact]
    public void Keyed_ValueDiffers_IsDiff()
    {
        var table = Build(
            OrgInfoSections.Users,
            Source(OrgA, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("1", "u1@x.com", "Standard"))),
            Source(OrgB, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("2", "u1@x.com", "Admin"))));

        Assert.True(table.Rows[0].IsDiff);
    }

    [Fact]
    public void Keyed_RecordTypes_CompositeKeyAndLabel()
    {
        var table = Build(
            OrgInfoSections.RecordTypes,
            Source(OrgA, OrgInfoSections.RecordTypes, Section(OrgInfoSections.RecordTypes,
                Row("id1", "Business", ("sobject", "Account"), ("developerName", "Business"), ("name", "Business Account")))),
            Source(OrgB, OrgInfoSections.RecordTypes, Section(OrgInfoSections.RecordTypes,
                Row("id2", "Business", ("sobject", "Account"), ("developerName", "Business"), ("name", "Business Account")))));

        Assert.Single(table.Rows);
        Assert.Equal("Account.Business", table.Rows[0].Key);
        Assert.Equal("Account.Business", table.Rows[0].Label);
        Assert.False(table.Rows[0].IsDiff);
    }

    [Fact]
    public void Keyed_FailedSection_CellIsFailed()
    {
        var table = Build(
            OrgInfoSections.Users,
            Source(OrgA, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("1", "u1@x.com", "Standard"))),
            Source(OrgB, OrgInfoSections.Users, null, OrgCompareCellState.Failed));

        Assert.Equal(OrgCompareCellState.Failed, table.Rows[0].Cells[1].State);
        Assert.False(table.Rows[0].IsDiff);
    }

    [Fact]
    public void Keyed_Owds_UsesRowIdAndDisplayColumn()
    {
        var table = Build(
            OrgInfoSections.Owds,
            Source(OrgA, OrgInfoSections.Owds, Section(OrgInfoSections.Owds,
                Row("org:Account", "Account", ("target", "Account"), ("internal", "Private"), ("external", "Private")))),
            Source(OrgB, OrgInfoSections.Owds, Section(OrgInfoSections.Owds,
                Row("org:Account", "Account", ("target", "Account"), ("internal", "PublicReadWrite"), ("external", "Private")))));

        Assert.Single(table.Rows);
        Assert.Equal("org:Account", table.Rows[0].Key);
        Assert.True(table.Rows[0].IsDiff);
    }

    // ---- Stats ----

    [Fact]
    public void Stats_ComputedFromSections_AndDiff()
    {
        var table = Build(
            OrgCompareCategories.StatsCategoryId,
            Source(OrgA, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("1", "u1@x.com", "Std"), UserRow("2", "u2@x.com", "Std"))),
            Source(OrgB, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("3", "u1@x.com", "Std"))));

        var totalUsers = table.Rows.Single(r => r.Key == "totalUsers");
        Assert.Equal("2", totalUsers.Cells[0].Text);
        Assert.Equal("1", totalUsers.Cells[1].Text);
        Assert.True(totalUsers.IsDiff);

        var activeUsers = table.Rows.Single(r => r.Key == "activeUsers");
        Assert.Equal("2", activeUsers.Cells[0].Text);
        Assert.Equal("1", activeUsers.Cells[1].Text);
        Assert.True(activeUsers.IsDiff);

        // プロファイル等は未取得 → NotFetched で差分なし
        var profiles = table.Rows.Single(r => r.Key == "profiles");
        Assert.Equal(OrgCompareCellState.NotFetched, profiles.Cells[0].State);
        Assert.False(profiles.IsDiff);
    }

    [Fact]
    public void Stats_AllNotFetched_NoDiff()
    {
        var table = Build(OrgCompareCategories.StatsCategoryId);

        Assert.Equal(OrgInfoCatalog.Stats.Count, table.Rows.Count);
        Assert.All(table.Rows, r => Assert.False(r.IsDiff));
        Assert.All(table.Rows, r => Assert.All(r.Cells, c => Assert.Equal(OrgCompareCellState.NotFetched, c.State)));
    }

    // ---- 共通ロジック ----

    [Fact]
    public void ComputeKey_FallsBackToRowId_WhenPartMissing()
    {
        var category = Category(OrgInfoSections.RecordTypes);
        var row = Row("id1", "Business", ("sobject", "Account"), ("developerName", null));

        Assert.Equal("id1", OrgCompareService.ComputeKey(category, row));
    }

    [Fact]
    public void ComputeKey_TrimParts()
    {
        var category = Category(OrgInfoSections.PermissionSets);
        var row = Row("id1", "PS", ("apiName", "  My_PS  "));

        Assert.Equal("My_PS", OrgCompareService.ComputeKey(category, row));
    }

    [Fact]
    public void BuildDataTable_HeadersDiffMarkAndMissing()
    {
        var table = Build(
            OrgInfoSections.Users,
            Source(OrgA, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("1", "u1@x.com", "Standard"), UserRow("2", "u2@x.com", "Standard"))),
            Source(OrgB, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("3", "u1@x.com", "Standard"))));

        var dataTable = OrgCompareService.BuildDataTable(table);

        Assert.Equal(4, dataTable.Columns.Count);
        Assert.Equal("Org A", dataTable.Columns[1].ColumnName);
        Assert.Equal("Org B", dataTable.Columns[2].ColumnName);

        var missingRow = dataTable.Rows.Cast<System.Data.DataRow>().Single(r => (string)r[0] == "u2@x.com");
        Assert.Equal(OrgCompareService.MissingText, missingRow[2]);
        Assert.Equal(OrgCompareService.DiffMark, missingRow[3]);
    }

    [Fact]
    public void BuildDataTable_DiffOnly_FiltersRows()
    {
        var table = Build(
            OrgInfoSections.Users,
            Source(OrgA, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("1", "u1@x.com", "Standard"), UserRow("2", "u2@x.com", "Standard"))),
            Source(OrgB, OrgInfoSections.Users, Section(OrgInfoSections.Users, UserRow("3", "u1@x.com", "Standard"))));

        var dataTable = OrgCompareService.BuildDataTable(table, diffOnly: true);

        Assert.Single(dataTable.Rows);
        Assert.Equal("u2@x.com", dataTable.Rows[0][0]);
    }

    [Fact]
    public void Categories_All_TitleKeysExist()
    {
        foreach (var category in OrgCompareCategories.All)
        {
            Assert.True(UiText.HasKey(category.TitleKey), $"missing key: {category.TitleKey}");
        }
    }

    [Fact]
    public void Categories_RequiredSectionIds_ForStats()
    {
        var category = Category(OrgCompareCategories.StatsCategoryId);

        Assert.Equal(
            new[] { OrgInfoSections.Users, OrgInfoSections.Profiles, OrgInfoSections.PermissionSets, OrgInfoSections.Roles, OrgInfoSections.Objects },
            OrgCompareCategories.RequiredSectionIds(category));
    }

    [Fact]
    public void Categories_RequiredSectionIds_ForSectionCategory()
    {
        Assert.Equal(new[] { OrgInfoSections.Roles }, OrgCompareCategories.RequiredSectionIds(Category(OrgInfoSections.Roles)));
    }
}
