using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

[Collection("Localization")]
public class ImportFieldMatcherTests
{
    private static DataIoField F(string name, string label, bool createable = true, bool updateable = true, bool externalId = false, bool nillable = true, bool defaultedOnCreate = false) =>
        new(name, label, "string", createable, updateable, nillable, defaultedOnCreate, externalId, false, Array.Empty<string>());

    private static DataIoObjectDescribe Describe(params DataIoField[] fields) => new("Account", "Account", fields);

    [Fact]
    public void Suggest_MatchesApiNameCaseInsensitive_ThenLabel()
    {
        var describe = Describe(F("Name", "Account Name"), F("BillingCity", "請求先市区町村"));

        var mappings = ImportFieldMatcher.Suggest(
            new[] { "name", "請求先市区町村", "Unknown" },
            describe,
            DataImportOperation.Insert,
            null);

        Assert.Equal("Name", mappings[0].FieldName);
        Assert.Equal("BillingCity", mappings[1].FieldName);
        Assert.Null(mappings[2].FieldName);
        Assert.False(mappings[2].IsMapped);
    }

    [Fact]
    public void Suggest_Update_MapsIdColumn()
    {
        var describe = Describe(F("Name", "Account Name"));

        var mappings = ImportFieldMatcher.Suggest(
            new[] { "Id", "Name" },
            describe,
            DataImportOperation.Update,
            null);

        Assert.Equal("Id", mappings[0].FieldName);
        Assert.True(mappings[0].IsMapped);
        Assert.Equal("Name", mappings[1].FieldName);
    }

    [Fact]
    public void Suggest_Delete_OnlyIdIsMappable()
    {
        var describe = Describe(F("Name", "Account Name"));

        var mappings = ImportFieldMatcher.Suggest(
            new[] { "Id", "Name" },
            describe,
            DataImportOperation.Delete,
            null);

        Assert.Equal("Id", mappings[0].FieldName);
        Assert.Null(mappings[1].FieldName);
    }

    [Fact]
    public void Suggest_Upsert_MapsExternalIdField_EvenIfNotCreateable()
    {
        var describe = Describe(F("ExtId__c", "External Id", createable: false, updateable: false, externalId: true), F("Name", "Name"));

        var mappings = ImportFieldMatcher.Suggest(
            new[] { "ExtId__c", "Name" },
            describe,
            DataImportOperation.Upsert,
            "ExtId__c");

        Assert.Equal("ExtId__c", mappings[0].FieldName);
        Assert.Equal("Name", mappings[1].FieldName);
    }

    [Fact]
    public void Suggest_DuplicateColumns_OnlyFirstMapped()
    {
        var describe = Describe(F("Name", "Name"));

        var mappings = ImportFieldMatcher.Suggest(
            new[] { "Name", "name" },
            describe,
            DataImportOperation.Insert,
            null);

        Assert.Equal("Name", mappings[0].FieldName);
        Assert.Null(mappings[1].FieldName);
    }

    [Fact]
    public void CandidateFields_Insert_UsesCreateableFields()
    {
        var describe = Describe(F("Name", "Name", createable: true), F("Formula__c", "Formula", createable: false));

        var fields = ImportFieldMatcher.CandidateFields(describe, DataImportOperation.Insert, null);

        Assert.Single(fields);
        Assert.Equal("Name", fields[0].Name);
    }

    [Fact]
    public void CandidateFields_Update_PrependsId()
    {
        var describe = Describe(F("Name", "Name", updateable: true), F("Auto__c", "Auto", updateable: false));

        var fields = ImportFieldMatcher.CandidateFields(describe, DataImportOperation.Update, null);

        Assert.Equal(2, fields.Count);
        Assert.Equal("Id", fields[0].Name);
        Assert.Equal("Name", fields[1].Name);
    }

    [Fact]
    public void CandidateFields_SkipsIdFromDescribe()
    {
        var describe = Describe(F("Id", "Record ID"), F("Name", "Name"));

        var fields = ImportFieldMatcher.CandidateFields(describe, DataImportOperation.Insert, null);

        // describe の Id は除外され、挿入時は Id 候補なし
        Assert.Single(fields);
        Assert.Equal("Name", fields[0].Name);
    }

    [Fact]
    public void Validate_Checks()
    {
        var mappings = new[] { new ImportColumnMapping(0, "Name", "Name", true) };

        Assert.Equal(UiText.T("DataImport_Err_NoRows"), ImportFieldMatcher.Validate(mappings, 0, DataImportOperation.Insert, null));
        Assert.Equal(UiText.T("DataImport_Err_NoMapped"), ImportFieldMatcher.Validate(new[] { new ImportColumnMapping(0, "X", null, true) }, 1, DataImportOperation.Insert, null));
        Assert.Equal(UiText.T("DataImport_Err_IdRequired"), ImportFieldMatcher.Validate(mappings, 1, DataImportOperation.Update, null));
        Assert.Equal(UiText.T("DataImport_Err_IdRequired"), ImportFieldMatcher.Validate(mappings, 1, DataImportOperation.Delete, null));
        Assert.Equal(UiText.T("DataImport_Err_ExtIdRequired"), ImportFieldMatcher.Validate(mappings, 1, DataImportOperation.Upsert, null));
        Assert.Equal(UiText.T("DataImport_Err_ExtIdMapRequired"), ImportFieldMatcher.Validate(mappings, 1, DataImportOperation.Upsert, "ExtId__c"));
        Assert.Null(ImportFieldMatcher.Validate(mappings, 1, DataImportOperation.Insert, null));

        var withId = new[] { new ImportColumnMapping(0, "Id", "Id", true), new ImportColumnMapping(1, "Name", "Name", true) };
        Assert.Null(ImportFieldMatcher.Validate(withId, 1, DataImportOperation.Update, null));
    }

    [Fact]
    public void MissingRequiredFields_ReportsUnmappedRequired()
    {
        var describe = Describe(
            F("LastName", "Last Name", nillable: false),
            F("FirstName", "First Name"),
            F("Defaulted__c", "Defaulted", nillable: false, defaultedOnCreate: true));

        var mappings = new[] { new ImportColumnMapping(0, "FirstName", "FirstName", true) };

        var missing = ImportFieldMatcher.MissingRequiredFields(mappings, describe);

        Assert.Single(missing);
        Assert.Equal("LastName", missing[0].Name);
    }

    [Fact]
    public void ExternalIdFields_ReturnsExternalIdOnly()
    {
        var describe = Describe(F("Ext__c", "Ext", externalId: true), F("Name", "Name"));

        var fields = ImportFieldMatcher.ExternalIdFields(describe);

        Assert.Single(fields);
        Assert.Equal("Ext__c", fields[0].Name);
    }
}
