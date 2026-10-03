using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

[Collection("Localization")]
public class ImportBatchPlannerTests
{
    private static DataIoField F(string name, string type = "string", bool createable = true, bool updateable = true, bool externalId = false) =>
        new(name, name, type, createable, updateable, true, false, externalId, false, Array.Empty<string>());

    private static DataIoObjectDescribe Describe(params DataIoField[] fields) => new("Account", "Account", fields);

    private static ImportColumnMapping M(int index, string column, string? field, bool include = true) =>
        new(index, column, field, include);

    [Fact]
    public void BuildPlan_CoercesValues_AndSeparatesId()
    {
        var describe = Describe(F("Name"), F("Amount__c", "double"));
        var mappings = new[] { M(0, "Id", "Id"), M(1, "Name", "Name"), M(2, "Amount__c", "Amount__c") };
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "001AAA", "Acme", "12.5" },
            new[] { "002AAA", "Globex", "bad" },
        };

        var plan = ImportBatchPlanner.BuildPlan(rows, mappings, describe, DataImportOperation.Update, null, emptyAsNull: false);

        Assert.Equal("001AAA", plan.Rows[0].Id);
        Assert.Equal("Acme", plan.Rows[0].Fields["Name"]);
        Assert.Equal(12.5m, plan.Rows[0].Fields["Amount__c"]);
        Assert.Null(plan.Rows[0].Error);
        Assert.NotNull(plan.Rows[1].Error);
        Assert.Contains("bad", plan.Rows[1].Error!);
    }

    [Fact]
    public void BuildPlan_UpdateWithoutId_MarksRowError()
    {
        var describe = Describe(F("Name"));
        var mappings = new[] { M(0, "Id", "Id"), M(1, "Name", "Name") };
        var rows = new List<IReadOnlyList<string>> { new[] { "", "Acme" } };

        var plan = ImportBatchPlanner.BuildPlan(rows, mappings, describe, DataImportOperation.Update, null, emptyAsNull: false);

        Assert.NotNull(plan.Rows[0].Error);
    }

    [Fact]
    public void BuildPlan_Upsert_CapturesExternalIdValue()
    {
        var describe = Describe(F("ExtId__c", externalId: true), F("Name"));
        var mappings = new[] { M(0, "ExtId__c", "ExtId__c"), M(1, "Name", "Name") };
        var rows = new List<IReadOnlyList<string>> { new[] { "EXT-1", "Acme" }, new[] { "", "Globex" } };

        var plan = ImportBatchPlanner.BuildPlan(rows, mappings, describe, DataImportOperation.Upsert, "ExtId__c", emptyAsNull: false);

        Assert.Equal("EXT-1", plan.Rows[0].ExternalIdValue);
        Assert.Null(plan.Rows[0].Error);
        Assert.NotNull(plan.Rows[1].Error);
    }

    [Fact]
    public void BuildPlan_EmptyCell_OmitVersusNull()
    {
        var describe = Describe(F("Name"), F("City"));
        var mappings = new[] { M(0, "Name", "Name"), M(1, "City", "City") };
        var rows = new List<IReadOnlyList<string>> { new[] { "Acme", "" } };

        var omitted = ImportBatchPlanner.BuildPlan(rows, mappings, describe, DataImportOperation.Insert, null, emptyAsNull: false);
        var nulled = ImportBatchPlanner.BuildPlan(rows, mappings, describe, DataImportOperation.Insert, null, emptyAsNull: true);

        Assert.False(omitted.Rows[0].Fields.ContainsKey("City"));
        Assert.True(nulled.Rows[0].Fields.ContainsKey("City"));
        Assert.Null(nulled.Rows[0].Fields["City"]);
    }

    [Fact]
    public void BuildPlan_UncheckedMapping_IsIgnored()
    {
        var describe = Describe(F("Name"), F("City"));
        var mappings = new[] { M(0, "Name", "Name"), M(1, "City", "City", include: false) };
        var rows = new List<IReadOnlyList<string>> { new[] { "Acme", "Tokyo" } };

        var plan = ImportBatchPlanner.BuildPlan(rows, mappings, describe, DataImportOperation.Insert, null, emptyAsNull: false);

        Assert.False(plan.Rows[0].Fields.ContainsKey("City"));
    }

    [Fact]
    public void ChunkSendable_Splits200_AndSkipsErrors()
    {
        var rows = Enumerable.Range(0, 250)
            .Select(i => new ImportPlannedRow(i, new Dictionary<string, object?> { ["Name"] = "N" + i }, null, null, i == 3 ? "error" : null))
            .ToList();

        var batches = ImportBatchPlanner.ChunkSendable(rows);

        Assert.Equal(2, batches.Count);
        Assert.Equal(200, batches[0].Count);
        Assert.Equal(49, batches[1].Count);
        Assert.DoesNotContain(batches.SelectMany(b => b), r => r.RowIndex == 3);
    }

    [Fact]
    public void BuildCompositeBody_IncludesIdForUpdate()
    {
        var rows = new[]
        {
            new ImportPlannedRow(0, new Dictionary<string, object?> { ["Name"] = "Acme" }, "001AAA", null, null),
        };

        var body = ImportBatchPlanner.BuildCompositeBody(rows, "Account", includeId: false);
        using (var document = JsonDocument.Parse(body))
        {
            var root = document.RootElement;
            Assert.False(root.GetProperty("allOrNone").GetBoolean());
            var record = root.GetProperty("records")[0];
            Assert.Equal("attributes", record.EnumerateObject().First().Name);
            Assert.Equal("Account", record.GetProperty("attributes").GetProperty("type").GetString());
            Assert.False(record.TryGetProperty("Id", out _));
            Assert.Equal("Acme", record.GetProperty("Name").GetString());
        }

        var updateBody = ImportBatchPlanner.BuildCompositeBody(rows, "Account", includeId: true);
        using (var document = JsonDocument.Parse(updateBody))
        {
            Assert.Equal("001AAA", document.RootElement.GetProperty("records")[0].GetProperty("Id").GetString());
        }
    }

    [Fact]
    public void BuildBulkCsv_BuildsHeadersAndEscapesValues()
    {
        var rows = new[]
        {
            new ImportPlannedRow(0, new Dictionary<string, object?> { ["Name"] = "Acme, Inc.", ["Active__c"] = true }, "001AAA", null, null),
            new ImportPlannedRow(1, new Dictionary<string, object?> { ["Name"] = "Skip" }, null, null, "error"),
        };

        var (csv, headers) = ImportBatchPlanner.BuildBulkCsv(rows, DataImportOperation.Update, null);

        Assert.Equal(new[] { "Id", "Name", "Active__c" }, headers);
        var lines = csv.TrimEnd().Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Id,Name,Active__c", lines[0]);
        Assert.Contains("\"Acme, Inc.\"", lines[1]);
        Assert.Contains("true", lines[1]);
    }

    [Fact]
    public void BuildBulkCsv_Delete_OnlyIdColumn()
    {
        var rows = new[] { new ImportPlannedRow(0, new Dictionary<string, object?>(), "001AAA", null, null) };

        var (csv, headers) = ImportBatchPlanner.BuildBulkCsv(rows, DataImportOperation.Delete, null);

        Assert.Equal(new[] { "Id" }, headers);
        Assert.Contains("001AAA", csv);
    }

    [Fact]
    public void FormatCell_UsesInvariantBooleans()
    {
        Assert.Equal("true", ImportBatchPlanner.FormatCell(true));
        Assert.Equal("false", ImportBatchPlanner.FormatCell(false));
        Assert.Equal("12.5", ImportBatchPlanner.FormatCell(12.5m));
        Assert.Null(ImportBatchPlanner.FormatCell(null));
    }
}
