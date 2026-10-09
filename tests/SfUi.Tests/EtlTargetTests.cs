using System.Text;
using System.Text.Json;
using SfUi.Etl.Engine;
using SfUi.Etl.Staging;
using SfUi.Etl.Targets;
using Xunit;

namespace SfUi.Tests;

/// <summary>出力ターゲット（CSV ファイル / Salesforce ペイロード・応答解析）を検証する。</summary>
public class EtlTargetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-etl-target-" + Guid.NewGuid().ToString("N"));

    public EtlTargetTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
        }
    }

    private static QueueRow Row(long id, params object?[] values)
        => new(id, values, RowOp.Insert, QueueStatus.Pending, 0, null, null, null);

    [Fact]
    public async Task CsvFileTarget_WritesHeaderAndEscapedRows()
    {
        var path = Path.Combine(_dir, "out.csv");
        var target = new CsvFileTarget(path, new[] { "Name", "Memo" });

        using var store = RunStagingStore.Create(_dir, "csvrun");
        var context = new EtlApplyContext { Store = store, StepId = "s1", ObjectName = "t" };

        Assert.True(await target.TestAsync(default));

        var result = await target.ApplyBatchAsync(context, new[]
        {
            Row(1, "太郎", "a,b"),
            Row(2, "花子", "say \"hi\""),
            Row(3, null, "line1\nline2"),
        }, default);

        Assert.All(result.Rows, r => Assert.True(r.Success));

        var text = File.ReadAllText(path, new UTF8Encoding(false));
        Assert.StartsWith("Name,Memo\r\n", text);
        Assert.Contains("\"a,b\"", text);
        Assert.Contains("\"say \"\"hi\"\"\"", text);
        Assert.Contains("\"line1\nline2\"", text);

        // 追記時はヘッダーを繰り返さない
        await target.ApplyBatchAsync(context, new[] { Row(4, "次", "x") }, default);
        var lines = File.ReadAllLines(path);
        Assert.Equal(1, lines.Count(l => l.StartsWith("Name,Memo", StringComparison.Ordinal)));
        Assert.Contains(lines, l => l.StartsWith("次,", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CsvFileTarget_DeleteReportsError()
    {
        var path = Path.Combine(_dir, "out2.csv");
        var target = new CsvFileTarget(path, new[] { "Name" });
        using var store = RunStagingStore.Create(_dir, "csvrun2");
        var context = new EtlApplyContext { Store = store, StepId = "s1", ObjectName = "t" };

        var deleteRow = new QueueRow(1, new object?[] { "x" }, RowOp.Delete, QueueStatus.Pending, 0, null, null, null);
        var result = await target.ApplyBatchAsync(context, new[] { deleteRow }, default);

        Assert.False(result.Rows[0].Success);
        Assert.Contains("未対応", result.Rows[0].Error);
    }

    [Fact]
    public void InsertBody_BuildsCompositeRecords()
    {
        var body = SalesforcePayloads.BuildInsertBody(
            "Contact",
            new[] { "Name", "Age__c" },
            new[] { Row(1, "太郎", 30L), Row(2, "花子", 28L) });

        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("allOrNone").GetBoolean());

        var records = document.RootElement.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());
        Assert.Equal("Contact", records[0].GetProperty("attributes").GetProperty("type").GetString());
        Assert.Equal("太郎", records[0].GetProperty("Name").GetString());
        Assert.Equal(30L, records[0].GetProperty("Age__c").GetInt64());
        Assert.Equal("花子", records[1].GetProperty("Name").GetString());
    }

    [Fact]
    public void InsertBody_ConvertsByFieldType()
    {
        var types = new Dictionary<string, string>
        {
            ["Active__c"] = "boolean",
            ["StartDate__c"] = "date",
            ["Created__c"] = "datetime",
        };

        var body = SalesforcePayloads.BuildInsertBody(
            "X",
            new[] { "Active__c", "StartDate__c", "Created__c" },
            new[] { Row(1, 1L, "2026/10/09", "2026-10-09 12:34:56") },
            types);

        using var document = JsonDocument.Parse(body);
        var record = document.RootElement.GetProperty("records")[0];
        Assert.True(record.GetProperty("Active__c").GetBoolean());
        Assert.Equal("2026-10-09", record.GetProperty("StartDate__c").GetString());
        Assert.Equal("2026-10-09T12:34:56", record.GetProperty("Created__c").GetString());
    }

    [Fact]
    public void UpdateBody_IncludesId()
    {
        var updates = new List<(QueueRow Row, string Id)> { (Row(1, "太郎"), "001AAA") };
        var body = SalesforcePayloads.BuildUpdateBody("Contact", new[] { "Name" }, updates);

        using var document = JsonDocument.Parse(body);
        var record = document.RootElement.GetProperty("records")[0];
        Assert.Equal("001AAA", record.GetProperty("Id").GetString());
        Assert.Equal("太郎", record.GetProperty("Name").GetString());
    }

    [Fact]
    public void ParseCompositeResponse_HandlesSuccessAndError()
    {
        var json = """
        [{"id":"001","success":true,"errors":[]},
         {"success":false,"errors":[{"statusCode":"DUPLICATE_VALUE","message":"duplicate value found"}],"id":null}]
        """;

        var parsed = SalesforcePayloads.ParseCompositeResponse(json);

        Assert.Equal(2, parsed.Count);
        Assert.True(parsed[0].Success);
        Assert.Equal("001", parsed[0].Id);
        Assert.False(parsed[1].Success);
        Assert.Equal("DUPLICATE_VALUE", parsed[1].StatusCode);
        Assert.Equal("duplicate value found", parsed[1].Message);
    }

    [Fact]
    public void TransientClassification()
    {
        Assert.True(SalesforcePayloads.IsTransientStatus("UNABLE_TO_LOCK_ROW"));
        Assert.True(SalesforcePayloads.IsTransientStatus("REQUEST_LIMIT_EXCEEDED"));
        Assert.False(SalesforcePayloads.IsTransientStatus("INVALID_TYPE"));
        Assert.False(SalesforcePayloads.IsTransientStatus(null));
    }

    [Fact]
    public void ResolveSoql_EscapesQuotes()
    {
        var soql = SalesforcePayloads.BuildResolveSoql(
            "Account",
            "ExtId__c",
            new[] { "Name", "ExtId__c" },
            new[] { "A'1", "B" });

        Assert.Equal("SELECT Id, Name, ExtId__c FROM Account WHERE ExtId__c IN ('A\\'1', 'B')", soql);
    }

    [Fact]
    public void ParseFieldTypes_FromDescribe()
    {
        var json = """{"name":"Account","fields":[{"name":"Name","type":"string"},{"name":"Active__c","type":"boolean"}]}""";
        using var document = JsonDocument.Parse(json);

        var types = SalesforcePayloads.ParseFieldTypes(document.RootElement);

        Assert.Equal("string", types["Name"]);
        Assert.Equal("boolean", types["Active__c"]);
    }

    [Fact]
    public void RecordToJson_KeepsSubsetOnly()
    {
        var json = """{"Id":"001","Name":"A","Extra":"x"}""";
        using var document = JsonDocument.Parse(json);

        var output = SalesforcePayloads.RecordToJson(document.RootElement, new[] { "Id", "Name" });

        Assert.Equal("""{"Id":"001","Name":"A"}""", output);
    }

    [Fact]
    public void ToJsonValue_Conversions()
    {
        Assert.Equal(true, SalesforcePayloads.ToJsonValue(1L, "boolean"));
        Assert.Equal(false, SalesforcePayloads.ToJsonValue("0", "boolean"));
        Assert.Equal("2026-10-09", SalesforcePayloads.ToJsonValue(new DateTime(2026, 10, 9), "date"));
        Assert.Equal("2026-10-09T00:00:00", SalesforcePayloads.ToJsonValue(new DateTime(2026, 10, 9), "datetime"));
        Assert.Equal(30L, SalesforcePayloads.ToJsonValue(30L, "int"));
        Assert.Null(SalesforcePayloads.ToJsonValue(null, "boolean"));
    }
}
