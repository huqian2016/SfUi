using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public sealed class RecordAccessServiceTests
{
    private static IReadOnlyList<JsonElement> Rows(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    [Fact]
    public void ParseUsers_MapsIdNameUsername()
    {
        var users = RecordAccessService.ParseUsers(Rows(
            """
            [
              { "Id": "005A", "Name": "山田 太郎", "Username": "taro@example.com" },
              { "Id": "005B", "Name": "Bob", "Username": "bob@example.com" }
            ]
            """));

        Assert.Equal(2, users.Count);
        Assert.Equal("taro@example.com", users[0].Username);
        Assert.Equal("山田 太郎 (taro@example.com)", users[0].Display);
    }

    [Fact]
    public void BuildAccessQuery_UsesSingleUserAndInList()
    {
        var query = RecordAccessService.BuildAccessQuery("005A", new[] { "001X", "001Y" });

        Assert.Equal(
            "SELECT RecordId, HasReadAccess, HasEditAccess, HasDeleteAccess, HasTransferAccess " +
            "FROM UserRecordAccess WHERE UserId = '005A' AND RecordId IN ('001X', '001Y')",
            query);
    }

    [Fact]
    public void ParseAccessRows_MapsFlags()
    {
        var rows = RecordAccessService.ParseAccessRows(Rows(
            """
            [
              { "RecordId": "001X", "HasReadAccess": true, "HasEditAccess": false, "HasDeleteAccess": true, "HasTransferAccess": false },
              { "RecordId": "001Y", "HasReadAccess": false, "HasEditAccess": false, "HasDeleteAccess": false, "HasTransferAccess": true }
            ]
            """));

        Assert.Equal(2, rows.Count);
        Assert.Equal("001X", rows[0].RecordId);
        Assert.True(rows[0].Flags.Read);
        Assert.False(rows[0].Flags.Edit);
        Assert.True(rows[0].Flags.Delete);
        Assert.False(rows[0].Flags.Transfer);
        Assert.True(rows[1].Flags.Transfer);
    }

    [Fact]
    public void ChunkIds_SplitsAt200()
    {
        var ids = Enumerable.Range(0, 450).Select(i => $"001{i:D3}").ToList();

        var chunks = RecordAccessService.ChunkIds(ids);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(200, chunks[0].Count);
        Assert.Equal(200, chunks[1].Count);
        Assert.Equal(50, chunks[2].Count);
        Assert.Equal("001000", chunks[0][0]);
        Assert.Equal("001449", chunks[2][^1]);
    }

    [Fact]
    public void ChunkIds_NoRemainder_ReturnsSingleChunk()
    {
        var ids = Enumerable.Range(0, 200).Select(i => $"001{i:D3}").ToList();

        var chunks = RecordAccessService.ChunkIds(ids);

        Assert.Single(chunks);
        Assert.Equal(200, chunks[0].Count);
    }

    [Fact]
    public void BuildRecordQueryResult_ExcludesAttributesAndConvertsValues()
    {
        var result = RecordAccessService.BuildRecordQueryResult(
            Rows(
                """
                [
                  { "attributes": { "type": "Account", "url": "/x" }, "Id": "001X", "Name": "Acme", "AnnualRevenue": 1000.5, "Active__c": true, "Note__c": null },
                  { "attributes": { "type": "Account", "url": "/y" }, "Id": "001Y", "Name": "Beta" }
                ]
                """),
            truncated: false);

        Assert.Equal(new[] { "Id", "Name", "AnnualRevenue", "Active__c", "Note__c" }, result.Columns);
        Assert.Equal("Acme", result.Records[0]["Name"]);
        Assert.Equal("1000.5", result.Records[0]["AnnualRevenue"]);
        Assert.Equal("true", result.Records[0]["Active__c"]);
        Assert.Null(result.Records[0]["Note__c"]);
        Assert.Null(result.Records[1]["AnnualRevenue"]);
        Assert.Equal("Account", result.Records[0][RecordAccessService.TypeKey]);
        Assert.Equal("Account", result.Records[1][RecordAccessService.TypeKey]);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void BuildRecordQueryResult_PassesThroughTruncated()
    {
        var result = RecordAccessService.BuildRecordQueryResult(Rows("""[ { "Id": "001X" } ]"""), truncated: true);

        Assert.True(result.Truncated);
    }
}
