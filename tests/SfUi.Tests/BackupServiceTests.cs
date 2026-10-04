using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public sealed class BackupServiceTests
{
    private static DataIoField Field(string name, string type, bool createable = true, bool updateable = true) =>
        new(name, name, type, createable, updateable, Nillable: true, DefaultedOnCreate: false, ExternalId: false, Custom: false, ReferenceTo: Array.Empty<string>());

    private static IReadOnlyDictionary<string, DataIoField> Fields(params DataIoField[] fields) =>
        fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void BuildAllFieldsSoql_ExcludesCompoundTypes_AndKeepsId()
    {
        var describe = new DataIoObjectDescribe("Account", "Account", new[]
        {
            Field("Id", "id"),
            Field("Name", "string"),
            Field("BillingAddress", "address"),
            Field("Location__c", "location"),
            Field("Photo__c", "base64"),
            Field("Custom__c", "string"),
        });

        var soql = BackupService.BuildAllFieldsSoql(describe);

        Assert.Equal("SELECT Id, Name, Custom__c FROM Account", soql);
    }

    [Fact]
    public void ParseJsonRecords_TypedValues_AndAttributesSkipped()
    {
        var records = BackupService.ParseJsonRecords(
            """
            [
              { "attributes": { "type": "Account" }, "Id": "001X", "Name": "Acme", "AnnualRevenue": 1234.5, "NumberOfEmployees": 10, "Active__c": true, "Note__c": null },
              { "Id": "001Y", "Name": "Beta" }
            ]
            """);

        Assert.Equal(2, records.Count);
        Assert.False(records[0].ContainsKey("attributes"));
        Assert.Equal("001X", records[0]["Id"]);
        Assert.Equal(1234.5m, records[0]["AnnualRevenue"]);
        Assert.Equal(10L, records[0]["NumberOfEmployees"]);
        Assert.Equal(true, records[0]["Active__c"]);
        Assert.Null(records[0]["Note__c"]);
    }

    [Fact]
    public void BuildFields_StripsId_SkipsNonWriteable_AndRemapsReferences()
    {
        var fields = Fields(
            Field("Name", "string"),
            Field("AccountId", "reference"),
            Field("ReadOnly__c", "string", createable: false, updateable: false),
            Field("Score__c", "double"));
        var record = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Id"] = "001OLD",
            ["Name"] = "Acme",
            ["AccountId"] = "001PARENT-OLD",
            ["ReadOnly__c"] = "x",
            ["Score__c"] = 12.5m,
        };
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal) { ["001PARENT-OLD"] = "001PARENT-NEW" };

        var insert = BackupService.BuildFields(record, fields, idMap, forInsert: true);

        Assert.False(insert.ContainsKey("Id"));
        Assert.False(insert.ContainsKey("ReadOnly__c"));
        Assert.Equal("Acme", insert["Name"]);
        Assert.Equal("001PARENT-NEW", insert["AccountId"]);
        Assert.Equal(12.5m, insert["Score__c"]);
    }

    [Fact]
    public void BuildFields_UpdateMode_SkipsNonUpdateable()
    {
        var fields = Fields(
            Field("Name", "string"),
            Field("Auto__c", "string", createable: true, updateable: false));
        var record = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Id"] = "001X",
            ["Name"] = "Beta",
            ["Auto__c"] = "computed",
        };

        var update = BackupService.BuildFields(record, fields, new Dictionary<string, string>(), forInsert: false);

        Assert.Single(update);
        Assert.Equal("Beta", update["Name"]);
    }

    [Fact]
    public void BuildFields_ReferenceNotInMap_IsKept()
    {
        var fields = Fields(Field("AccountId", "reference"));
        var record = new Dictionary<string, object?>(StringComparer.Ordinal) { ["AccountId"] = "001EXISTING" };

        var insert = BackupService.BuildFields(record, fields, new Dictionary<string, string>(), forInsert: true);

        Assert.Equal("001EXISTING", insert["AccountId"]);
    }

    [Fact]
    public void CoerceCsvValue_ConvertsByFieldType()
    {
        Assert.Equal((object?)12.5m, BackupService.CoerceCsvValue(Field("Amount", "double"), "12.5").Value);
        Assert.Equal((object?)true, BackupService.CoerceCsvValue(Field("Active", "boolean"), "true").Value);
        Assert.Null(BackupService.CoerceCsvValue(Field("Name", "string"), "").Value);
        var (value, error) = BackupService.CoerceCsvValue(Field("Amount", "double"), "abc");
        Assert.Null(value);
        Assert.NotNull(error);
        Assert.Equal("raw", BackupService.CoerceCsvValue(null, "raw").Value);
    }

    [Fact]
    public void ValueDisplay_FormatsInvariant()
    {
        Assert.Equal("12.5", BackupService.ValueDisplay(12.5m));
        Assert.Equal("true", BackupService.ValueDisplay(true));
        Assert.Equal("10", BackupService.ValueDisplay(10L));
        Assert.Null(BackupService.ValueDisplay(null));
    }

    [Fact]
    public void ValidateBackupId_RejectsPathTraversal()
    {
        Assert.True(BackupService.ValidateBackupId("20261004-153012"));
        Assert.True(BackupService.ValidateBackupId("20261004-153012-2"));
        Assert.False(BackupService.ValidateBackupId(null));
        Assert.False(BackupService.ValidateBackupId(""));
        Assert.False(BackupService.ValidateBackupId("..\\evil"));
        Assert.False(BackupService.ValidateBackupId("a/b"));
        Assert.False(BackupService.ValidateBackupId("a b"));
    }

    [Fact]
    public void BackupMetadata_RoundTrips()
    {
        var metadata = new BackupMetadata(
            "20261004-153012", "ラベル", "説明", new DateTimeOffset(2026, 10, 4, 15, 30, 12, TimeSpan.FromHours(9)),
            "00DXXX", "user@example.com", "acc (user@example.com)", "0.8.0",
            new[]
            {
                new BackupObjectInfo("Account", "取引先", 42, BackupEngine.Rest, "Account.json"),
                new BackupObjectInfo("Case", "ケース", 5, BackupEngine.Bulk, "Case.csv"),
            });

        var json = JsonSerializer.Serialize(metadata);
        var restored = JsonSerializer.Deserialize<BackupMetadata>(json);

        Assert.NotNull(restored);
        Assert.Equal(metadata.Id, restored!.Id);
        Assert.Equal(metadata.Label, restored.Label);
        Assert.Equal(metadata.OrgId, restored.OrgId);
        Assert.Equal(47, restored.TotalRecords);
        Assert.Equal(BackupEngine.Bulk, restored.Objects[1].Engine);
        Assert.Contains("\"Bulk\"", json);
    }

    [Fact]
    public void BackupStateStore_SelectedObjects_RoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "sfui-backup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = AppPaths.Resolve(dataRootOverride: root);
            var store = new BackupStateStore(paths, new AppLog(paths));

            Assert.Empty(store.GetSelectedObjects("user@example.com"));

            store.SetSelectedObjects("user@example.com", new[] { "Account", "Case", "account" });

            var selected = store.GetSelectedObjects("user@example.com");
            Assert.Equal(2, selected.Count);
            Assert.Contains("Account", selected);
            Assert.Contains("Case", selected);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BackupStateStore_Counts_MergeAndClear()
    {
        var root = Path.Combine(Path.GetTempPath(), "sfui-backup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = AppPaths.Resolve(dataRootOverride: root);
            var store = new BackupStateStore(paths, new AppLog(paths));

            store.UpdateCounts("user@example.com", new Dictionary<string, int> { ["Account"] = 10 }, fetched: false);
            store.UpdateCounts("user@example.com", new Dictionary<string, int> { ["Case"] = 3 }, fetched: true);

            var counts = store.GetCachedCounts("user@example.com");
            Assert.Equal(10, counts["Account"]);
            Assert.Equal(3, counts["Case"]);

            store.ClearCounts("user@example.com");
            Assert.Empty(store.GetCachedCounts("user@example.com"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildUndeleteEnvelope_ContainsSessionAndIds()
    {
        var xml = SalesforceSoapClient.BuildUndeleteEnvelope(new[] { "001A", "001B" }, "TOKEN<>&");

        Assert.Contains("<urn:sessionId>TOKEN&lt;&gt;&amp;</urn:sessionId>", xml);
        Assert.Contains("<urn:ids>001A</urn:ids><urn:ids>001B</urn:ids>", xml);
        Assert.Contains("urn:undelete", xml);
    }

    [Fact]
    public void ParseUndeleteResponse_ReadsSuccessAndErrors()
    {
        var xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns="urn:partner.soap.sforce.com">
              <soapenv:Body>
                <undeleteResponse>
                  <result><id>001A</id><success>true</success></result>
                  <result><errors><message>insufficient access</message><statusCode>INSUFFICIENT_ACCESS</statusCode></errors><id xsi:nil="true" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"/><success>false</success></result>
                </undeleteResponse>
              </soapenv:Body>
            </soapenv:Envelope>
            """;

        var results = SalesforceSoapClient.ParseUndeleteResponse(xml);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Success);
        Assert.Equal("001A", results[0].Id);
        Assert.False(results[1].Success);
        Assert.Equal("insufficient access", results[1].Error);
    }
}
