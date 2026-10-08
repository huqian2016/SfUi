using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgRecordCompareServiceTests
{
    private static readonly OrgCompareOrgColumn OrgA = new("keyA", "Org A", "a@example.com", "https://a.my.salesforce.com");
    private static readonly OrgCompareOrgColumn OrgB = new("keyB", "Org B", "b@example.com", "https://b.my.salesforce.com");

    private static readonly OrgRecordCompareRequest Request = new(
        "Account",
        "Name",
        new[]
        {
            new OrgRecordCompareField("Industry", "業種"),
            new OrgRecordCompareField("Type", "種別"),
        },
        Limit: 200);

    private static OrgRecordValue Record(string id, string key, params string?[] values) => new(id, key, values);

    [Fact]
    public void CreateRecords_builds_dynamic_category()
    {
        var category = OrgCompareCategories.CreateRecords("Account");

        Assert.Equal("records:Account", category.Id);
        Assert.Null(category.SectionId);
        Assert.Equal(OrgCompareKind.Keyed, category.Kind);
        Assert.True(OrgCompareCategories.IsDynamic("records:Account"));
        Assert.False(OrgCompareCategories.IsDynamic("records:"));
        Assert.True(OrgCompareCategories.IsDynamic("fields:Account"));
        Assert.False(OrgCompareCategories.IsDynamic("users"));
        Assert.False(OrgCompareCategories.IsDynamic(null));
    }

    [Fact]
    public void BuildSoql_selects_id_key_and_fields_without_duplicates()
    {
        var request = Request with
        {
            Fields = new[]
            {
                new OrgRecordCompareField("Name", "名前"),
                new OrgRecordCompareField("Industry", "業種"),
                new OrgRecordCompareField("industry", "大文字違いは重複扱い"),
            },
        };

        Assert.Equal("SELECT Id, Name, Industry FROM Account LIMIT 200", OrgRecordCompareService.BuildSoql(request));
    }

    [Fact]
    public void BuildSoql_clamps_limit()
    {
        var soql = OrgRecordCompareService.BuildSoql(Request with { Limit = 0 });
        Assert.EndsWith("LIMIT 200", soql, StringComparison.Ordinal);

        soql = OrgRecordCompareService.BuildSoql(Request with { Limit = 99999 });
        Assert.EndsWith($"LIMIT {OrgRecordCompareService.MaxLimit}", soql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSoql_rejects_invalid_identifiers()
    {
        Assert.Throws<ArgumentException>(() => OrgRecordCompareService.BuildSoql(Request with { KeyField = "Name; DROP TABLE" }));
        Assert.Throws<ArgumentException>(() => OrgRecordCompareService.BuildSoql(Request with { ObjectApiName = "1Account" }));
        Assert.Throws<ArgumentException>(() => OrgRecordCompareService.BuildSoql(Request with
        {
            Fields = new[] { new OrgRecordCompareField("Name, Id", "x") },
        }));
    }

    [Fact]
    public void BuildTable_matches_records_by_key_and_marks_diffs()
    {
        var results = new[]
        {
            new OrgRecordQueryResult("keyA", OrgCompareCellState.Value, new[]
            {
                Record("001A1", "Acme", "Tech", "Customer"),
                Record("001A2", "Beta", "Finance", "Customer"),
            }),
            new OrgRecordQueryResult("keyB", OrgCompareCellState.Value, new[]
            {
                Record("001B1", "acme", "Tech", "Customer"), // キーの大文字小文字は無視して突合
                Record("001B2", "Beta", "Finance", "Partner"),
                Record("001B3", "Gamma", "Health", "Customer"),
            }),
        };

        var table = OrgRecordCompareService.BuildTable(
            OrgCompareCategories.CreateRecords("Account"),
            new[] { OrgA, OrgB },
            results,
            Request);

        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(2, table.DiffCount);
        Assert.False(table.Rows.Single(r => r.Key == "Acme").IsDiff);
        Assert.True(table.Rows.Single(r => r.Key == "Beta").IsDiff);

        var gamma = table.Rows.Single(r => r.Key == "Gamma");
        Assert.Equal(OrgCompareCellState.Missing, gamma.Cells[0].State);
        Assert.Equal(OrgCompareCellState.Value, gamma.Cells[1].State);

        // セルテキストは「ラベル: 値」を ・ で連結、リンクはレコードページ
        var acme = table.Rows.Single(r => r.Key == "Acme");
        Assert.Equal($"業種: Tech{OrgCompareService.CellSeparator}種別: Customer", acme.Cells[0].Text);
        Assert.Equal("https://a.my.salesforce.com/lightning/r/Account/001A1/view", acme.Cells[0].Link);
        Assert.Equal("https://b.my.salesforce.com/lightning/r/Account/001B1/view", acme.Cells[1].Link);
    }

    [Fact]
    public void BuildTable_falls_back_to_record_id_when_key_is_empty()
    {
        var results = new[]
        {
            new OrgRecordQueryResult("keyA", OrgCompareCellState.Value, new[] { Record("001A1", "", "Tech", "Customer") }),
            new OrgRecordQueryResult("keyB", OrgCompareCellState.Value, new[] { Record("001B1", "Acme", "Tech", "Customer") }),
        };

        var table = OrgRecordCompareService.BuildTable(
            OrgCompareCategories.CreateRecords("Account"),
            new[] { OrgA, OrgB },
            results,
            Request);

        Assert.Equal(2, table.Rows.Count);
        Assert.Contains(table.Rows, r => r.Key == "001A1");
    }

    [Fact]
    public void BuildTable_marks_failed_org_cells_and_excludes_them_from_diff()
    {
        var results = new[]
        {
            new OrgRecordQueryResult("keyA", OrgCompareCellState.Value, new[] { Record("001A1", "Acme", "Tech", "Customer") }),
            new OrgRecordQueryResult("keyB", OrgCompareCellState.Failed, Array.Empty<OrgRecordValue>()),
        };

        var table = OrgRecordCompareService.BuildTable(
            OrgCompareCategories.CreateRecords("Account"),
            new[] { OrgA, OrgB },
            results,
            Request);

        Assert.Single(table.Rows);
        Assert.Equal(OrgCompareCellState.Failed, table.Rows[0].Cells[1].State);
        Assert.False(table.Rows[0].IsDiff);
    }
}
