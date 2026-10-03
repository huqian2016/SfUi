using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

[Collection("Localization")]
public class ImportResultMapperTests
{
    [Fact]
    public void FromCompositeResponse_MapsSuccessAndFailure()
    {
        var batch = new[]
        {
            new ImportPlannedRow(0, new Dictionary<string, object?>(), null, null, null),
            new ImportPlannedRow(1, new Dictionary<string, object?>(), null, null, null),
        };
        var json = """
            [
              { "id": "001AAA", "success": true, "errors": [] },
              { "success": false, "errors": [ { "statusCode": "REQUIRED_FIELD_MISSING", "message": "Required fields are missing: [Name]", "fields": ["Name"] } ] }
            ]
            """;

        var results = ImportResultMapper.FromCompositeResponse(json, batch);

        Assert.True(results[0].Success);
        Assert.Equal("001AAA", results[0].Id);
        Assert.False(results[1].Success);
        Assert.Contains("Required fields are missing", results[1].Error);
        Assert.Contains("[Name]", results[1].Error);
        Assert.Contains("REQUIRED_FIELD_MISSING", results[1].Error);
    }

    [Fact]
    public void FromCompositeResponse_ShortResponse_PadsFailures()
    {
        var batch = new[]
        {
            new ImportPlannedRow(0, new Dictionary<string, object?>(), null, null, null),
            new ImportPlannedRow(1, new Dictionary<string, object?>(), null, null, null),
        };

        var results = ImportResultMapper.FromCompositeResponse("""[ { "id": "001", "success": true } ]""", batch);

        Assert.Equal(2, results.Count);
        Assert.False(results[1].Success);
        Assert.Equal(UiText.T("DataIo_Err_NoResponse"), results[1].Error);
    }

    [Fact]
    public void FromSingleResponse_UpsertStyle()
    {
        var json = """{ "id": "001AAA", "success": true, "created": false, "errors": [] }""";

        var result = ImportResultMapper.FromSingleResponse(json, 7);

        Assert.Equal(7, result.RowIndex);
        Assert.True(result.Success);
        Assert.Equal("001AAA", result.Id);
    }

    [Theory]
    [InlineData("""{ "result": { "jobInfo": { "id": "750XX", "numberRecordsProcessed": 120, "numberRecordsFailed": 2 } } }""", "750XX", 120, 2)]
    [InlineData("""{ "jobInfo": { "id": "750YY", "numberRecordsProcessed": "5", "numberRecordsFailed": 0 } }""", "750YY", 5, 0)]
    [InlineData("""{ "id": "750ZZ" }""", "750ZZ", 0, 0)]
    public void ParseBulkJobInfo_Variants(string json, string? expectedId, int processed, int failed)
    {
        var (id, processedCount, failedCount) = ImportResultMapper.ParseBulkJobInfo(json);

        Assert.Equal(expectedId, id);
        Assert.Equal(processed, processedCount);
        Assert.Equal(failed, failedCount);
    }

    [Fact]
    public void ExtractRecords_SupportsResultAndRootShapes()
    {
        Assert.Single(ImportResultMapper.ExtractRecords("""{ "result": { "records": [ { "sf__Id": "1" } ] } }"""));
        Assert.Single(ImportResultMapper.ExtractRecords("""{ "records": [ { "sf__Id": "1" } ] }"""));
        Assert.Single(ImportResultMapper.ExtractRecords("""[ { "sf__Id": "1" } ]"""));
        Assert.Empty(ImportResultMapper.ExtractRecords("not json"));
    }

    [Fact]
    public void MapBulkFailures_MatchesRowsByValues()
    {
        var plan = new ImportPlan(new[]
        {
            new ImportPlannedRow(0, new Dictionary<string, object?> { ["Name"] = "Acme" }, null, null, null),
            new ImportPlannedRow(1, new Dictionary<string, object?> { ["Name"] = "Globex" }, null, null, null),
        });

        var records = ImportResultMapper.ExtractRecords("""
            { "records": [
              { "Name": "Acme", "sf__Id": "001AAA", "sf__Error": "" },
              { "Name": "Globex", "sf__Id": "", "sf__Error": "DUPLICATE_VALUE: duplicate" }
            ] }
            """);

        var failed = ImportResultMapper.MapBulkFailures(records, plan, new[] { "Name" });

        Assert.Single(failed);
        Assert.Equal(1, failed[0].RowIndex);
        Assert.Contains("DUPLICATE_VALUE", failed[0].Error);
    }

    [Fact]
    public void MapBulkFailures_NumericNormalization()
    {
        var plan = new ImportPlan(new[]
        {
            new ImportPlannedRow(4, new Dictionary<string, object?> { ["Amount__c"] = 1.0m }, null, null, null),
        });

        var records = ImportResultMapper.ExtractRecords("""
            { "records": [ { "Amount__c": "1.000", "sf__Error": "error" } ] }
            """);

        var failed = ImportResultMapper.MapBulkFailures(records, plan, new[] { "Amount__c" });

        Assert.Single(failed);
        Assert.Equal(4, failed[0].RowIndex);
    }

    [Fact]
    public void MapBulkFailures_UnmatchedRow_ReturnsMinusOne()
    {
        var plan = new ImportPlan(new[]
        {
            new ImportPlannedRow(0, new Dictionary<string, object?> { ["Name"] = "Acme" }, null, null, null),
        });

        var records = ImportResultMapper.ExtractRecords("""
            { "records": [ { "Name": "Other", "sf__Error": "error" } ] }
            """);

        var failed = ImportResultMapper.MapBulkFailures(records, plan, new[] { "Name" });

        Assert.Single(failed);
        Assert.Equal(-1, failed[0].RowIndex);
    }
}
