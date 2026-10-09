using System.Text.Json;
using SfUi.Etl.Sources;
using Xunit;

namespace SfUi.Tests;

public class SalesforceSourceTests
{
    [Fact]
    public void SinglePage_ParsesRecords_ExcludesAttributes()
    {
        var pages = new FakePages(
            """{"totalSize":2,"done":true,"records":[{"attributes":{"type":"Account","url":"/x"},"Id":"001A","Name":"Acme","AnnualRevenue":100,"Active":true,"Parent":null,"BillingAddress":{"City":"Tokyo"}},{"attributes":{"type":"Account"},"Id":"001B","Name":"Beta"}]}""");
        var source = Create("SELECT Id, Name FROM Account", pages);

        Assert.Equal(1, pages.QueryCalls);
        Assert.Equal(new[] { "Id", "Name", "AnnualRevenue", "Active", "Parent", "BillingAddress" }, source.Columns);

        var rows = source.ReadRows().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("001A", rows[0][0]);
        Assert.Equal("Acme", rows[0][1]);
        Assert.Equal("100", rows[0][2]);
        Assert.Equal("true", rows[0][3]);
        Assert.Null(rows[0][4]);
        Assert.Equal("""{"City":"Tokyo"}""", rows[0][5]);
        Assert.Equal("001B", rows[1][0]);
    }

    [Fact]
    public void Paging_FollowsNextRecordsUrl_UntilDone()
    {
        var pages = new FakePages(
            """{"totalSize":5,"done":false,"nextRecordsUrl":"/services/data/v67.0/query/01g-2000","records":[{"Id":"1"},{"Id":"2"},{"Id":"3"}]}""",
            """{"totalSize":5,"done":true,"records":[{"Id":"4"},{"Id":"5"}]}""");
        var source = Create("SELECT Id FROM Account", pages);

        Assert.Equal(1, pages.QueryCalls);
        Assert.Equal(new[] { "/services/data/v67.0/query/01g-2000" }, pages.NextUrls);
        Assert.Equal(5, source.ReadRows().Count());
    }

    [Fact]
    public void UnionColumns_AcrossPages()
    {
        var pages = new FakePages(
            """{"done":false,"nextRecordsUrl":"/n1","records":[{"A":"a1","B":"b1"}]}""",
            """{"done":true,"records":[{"B":"b2","C":"c2"}]}""");
        var source = Create("SELECT A, B, C FROM X", pages);

        Assert.Equal(new[] { "A", "B", "C" }, source.Columns);
        var rows = source.ReadRows().ToList();
        Assert.Equal("a1", rows[0][0]);
        Assert.Null(rows[1][0]);
        Assert.Equal("b2", rows[1][1]);
        Assert.Equal("c2", rows[1][2]);
    }

    [Fact]
    public void DoneFalseWithoutNext_Stops()
    {
        var pages = new FakePages("""{"done":false,"records":[{"Id":"1"}]}""");
        var source = Create("SELECT Id FROM Account", pages);

        Assert.Equal(1, pages.QueryCalls);
        Assert.Empty(pages.NextUrls);
        Assert.Single(source.ReadRows());
    }

    [Fact]
    public void MissingRecordsProperty_Throws()
    {
        var pages = new FakePages("""{"totalSize":0,"done":true}""");
        var ex = Assert.Throws<InvalidOperationException>(
            () => Create("SELECT Id FROM Account", pages));
        Assert.Contains("records", ex.Message);
    }

    [Fact]
    public void MaxPages_Throws()
    {
        var pages = new FakePages(
            """{"done":false,"nextRecordsUrl":"/n1","records":[{"Id":"1"}]}""",
            """{"done":false,"nextRecordsUrl":"/n2","records":[{"Id":"2"}]}""",
            """{"done":false,"nextRecordsUrl":"/n3","records":[{"Id":"3"}]}""");
        var ex = Assert.Throws<InvalidOperationException>(
            () => new SalesforceSource("SELECT Id FROM Account", pages, maxPages: 2));
        Assert.Contains("最大ページ数", ex.Message);
        Assert.Equal(1, pages.QueryCalls);
        Assert.Single(pages.NextUrls);
    }

    [Fact]
    public void Name_ParsesObjectFromSoql()
    {
        Assert.Equal("Account", Create("select Id from Account where Name = 'x'", new FakePages("{\"done\":true,\"records\":[]}")).Name);
        Assert.Equal("Account__c", Create("SELECT Id FROM Account__c", new FakePages("{\"done\":true,\"records\":[]}")).Name);
    }

    [Fact]
    public void Name_WithoutFrom_UsesSoql()
    {
        Assert.Equal("SOQL", Create("garbage query", new FakePages("{\"done\":true,\"records\":[]}")).Name);
    }

    [Fact]
    public void EmptySoql_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SalesforceSource("   ", new FakePages()));
    }

    private static SalesforceSource Create(string soql, FakePages pages) => new(soql, pages);

    private sealed class FakePages : ISoqlPages
    {
        private readonly Queue<string> _bodies;

        public FakePages(params string[] bodies) => _bodies = new Queue<string>(bodies);

        public int QueryCalls { get; private set; }

        public List<string> NextUrls { get; } = new();

        public Task<JsonDocument> QueryAsync(string soql, CancellationToken cancellationToken)
        {
            QueryCalls++;
            return Task.FromResult(JsonDocument.Parse(_bodies.Dequeue()));
        }

        public Task<JsonDocument> GetPageAsync(string nextRecordsUrl, CancellationToken cancellationToken)
        {
            NextUrls.Add(nextRecordsUrl);
            return Task.FromResult(JsonDocument.Parse(_bodies.Dequeue()));
        }
    }
}
