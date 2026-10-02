using System.Data;
using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class SoqlResultFactoryTests
{
    [Fact]
    public void FromJsonRecords_FlattensNestedObjectsAndSkipsAttributes()
    {
        var records = JsonSerializer.Deserialize<List<JsonElement>>("""
        [
          { "attributes": { "type": "Account", "url": "/x" }, "Id": "001", "Name": "Acme",
            "Account": { "attributes": { "type": "Account" }, "Name": "Parent" } },
          { "attributes": { "type": "Account", "url": "/y" }, "Id": "002", "Name": null, "Account": null }
        ]
        """)!;

        var result = SoqlResultFactory.FromJsonRecords(records, 2, true, "[]");
        var table = result.Table;

        Assert.Equal(2, result.TotalSize);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(
            new[] { "Id", "Name", "Account.Name", "Account" },
            table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray());

        Assert.Equal("Acme", table.Rows[0]["Name"]);
        Assert.Equal("Parent", table.Rows[0]["Account.Name"]);
        Assert.True(table.Rows[1]["Name"] is DBNull);
        Assert.True(table.Rows[1]["Account"] is DBNull);
        Assert.DoesNotContain("attributes", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
    }

    [Fact]
    public void FromJsonRecords_ArrayValues_SerializedAsJsonString()
    {
        var records = JsonSerializer.Deserialize<List<JsonElement>>("""[ { "Id": "001", "Values": [1, 2] } ]""")!;

        var table = SoqlResultFactory.FromJsonRecords(records, 1, true, "[]").Table;

        Assert.Equal("[1, 2]", table.Rows[0]["Values"]);
    }

    [Fact]
    public void FromJsonRecords_NumbersAndBooleans_AsRawText()
    {
        var records = JsonSerializer.Deserialize<List<JsonElement>>("""[ { "Amount": 12.5, "Active": true } ]""")!;

        var table = SoqlResultFactory.FromJsonRecords(records, 1, true, "[]").Table;

        Assert.Equal("12.5", table.Rows[0]["Amount"]);
        Assert.Equal("true", table.Rows[0]["Active"]);
    }

    [Fact]
    public void FromJsonRecords_EmptyRecords_ReturnsEmptyTable()
    {
        var result = SoqlResultFactory.FromJsonRecords(Array.Empty<JsonElement>(), 0, true, "[]");

        Assert.Empty(result.Table.Rows);
        Assert.Empty(result.Table.Columns);
    }
}
