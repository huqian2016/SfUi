using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>グローバルな言語状態に依存する文字列を検証するため、言語切替テストと直列実行する。</summary>
[Collection("Localization")]
public class SoqlEditPlannerTests
{
    private static DataIoField Field(string name, string type, bool updateable = true) =>
        new(name, name, type, true, updateable, true, false, false, false, Array.Empty<string>());

    private static IReadOnlyDictionary<string, DataIoField> Fields(params DataIoField[] fields) =>
        fields.ToDictionary(f => f.Name, StringComparer.Ordinal);

    [Fact]
    public void Plan_ConvertsValuesByFieldType()
    {
        var rows = new[]
        {
            new SoqlEditRow(0, "003XX", new Dictionary<string, string?>
            {
                ["Name"] = "Alice",
                ["AnnualRevenue"] = "1234.5",
                ["IsActive"] = "no",
            }),
        };

        var entry = Assert.Single(SoqlEditPlanner.Plan(rows, Fields(
            Field("Name", "string"),
            Field("AnnualRevenue", "currency"),
            Field("IsActive", "boolean"))));

        Assert.Null(entry.Error);
        Assert.NotNull(entry.Planned);
        Assert.Equal(0, entry.Planned!.RowIndex);
        Assert.Equal("003XX", entry.Planned.Id);
        Assert.Equal("Alice", entry.Planned.Fields["Name"]);
        Assert.Equal(1234.5m, entry.Planned.Fields["AnnualRevenue"]);
        Assert.Equal(false, entry.Planned.Fields["IsActive"]);
    }

    [Fact]
    public void Plan_EmptyValueBecomesNull()
    {
        var rows = new[] { new SoqlEditRow(2, "003XX", new Dictionary<string, string?> { ["Name"] = "" }) };

        var entry = Assert.Single(SoqlEditPlanner.Plan(rows, Fields(Field("Name", "string"))));

        Assert.Null(entry.Error);
        Assert.Null(entry.Planned!.Fields["Name"]);
        Assert.Equal(2, entry.Planned.RowIndex);
    }

    [Fact]
    public void Plan_KeepsRawStringForUnknownFields()
    {
        var rows = new[] { new SoqlEditRow(0, "003XX", new Dictionary<string, string?> { ["Custom__c"] = "abc" }) };

        var entry = Assert.Single(SoqlEditPlanner.Plan(rows, Fields(Field("Name", "string"))));

        Assert.Null(entry.Error);
        Assert.Equal("abc", entry.Planned!.Fields["Custom__c"]);
    }

    [Fact]
    public void Plan_WithoutDescribeSendsStrings()
    {
        var rows = new[] { new SoqlEditRow(0, "003XX", new Dictionary<string, string?> { ["Amount"] = "10" }) };

        var entry = Assert.Single(SoqlEditPlanner.Plan(rows, null));

        Assert.Null(entry.Error);
        Assert.Equal("10", entry.Planned!.Fields["Amount"]);
    }

    [Fact]
    public void Plan_ReportsRowWithoutId()
    {
        var rows = new[] { new SoqlEditRow(1, null, new Dictionary<string, string?> { ["Name"] = "x" }) };

        var entry = Assert.Single(SoqlEditPlanner.Plan(rows, null));

        Assert.Null(entry.Planned);
        Assert.Equal(UiText.T("Soql_Err_RowIdMissing"), entry.Error);
    }

    [Fact]
    public void Plan_ReportsNonUpdateableField()
    {
        var rows = new[] { new SoqlEditRow(0, "003XX", new Dictionary<string, string?> { ["Formula__c"] = "x" }) };

        var entry = Assert.Single(SoqlEditPlanner.Plan(rows, Fields(Field("Formula__c", "string", updateable: false))));

        Assert.Null(entry.Planned);
        Assert.Equal(UiText.T("Soql_Err_FieldNotUpdateableFmt", "Formula__c"), entry.Error);
    }

    [Fact]
    public void Plan_ReportsConversionError()
    {
        var rows = new[] { new SoqlEditRow(0, "003XX", new Dictionary<string, string?> { ["Amount"] = "abc" }) };

        var entry = Assert.Single(SoqlEditPlanner.Plan(rows, Fields(Field("Amount", "double"))));

        Assert.Null(entry.Planned);
        Assert.NotNull(entry.Error);
        Assert.Contains("abc", entry.Error);
    }
}
