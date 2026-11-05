using System.Text.Json;
using SfUi.App.ViewModels;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>匿名 Apex の DML 文生成（純関数）のテスト。</summary>
public class ApexDmlGeneratorTests
{
    // ---- 生成 ----

    [Fact]
    public void Insert_GeneratesAssignmentAndInsert()
    {
        var code = ApexDmlGenerator.Generate(new ApexDmlRequest(
            "Account",
            ApexDmlKinds.Insert,
            null,
            new[]
            {
                new ApexDmlFieldValue("Name", "string", "Acme"),
                new ApexDmlFieldValue("NumberOfEmployees", "int", "10"),
            }));

        Assert.Contains("Account recAccount = new Account();", code);
        Assert.Contains("recAccount.Name = 'Acme';", code);
        Assert.Contains("recAccount.NumberOfEmployees = 10;", code);
        Assert.Contains("insert recAccount;", code);
        Assert.DoesNotContain("Id recAccountId", code);
    }

    [Fact]
    public void Update_GeneratesIdAndUpdate()
    {
        var code = ApexDmlGenerator.Generate(new ApexDmlRequest(
            "Account",
            ApexDmlKinds.Update,
            "001000000000001AAA",
            new[] { new ApexDmlFieldValue("Name", "string", "Acme") }));

        Assert.Contains("Id recAccountId = '001000000000001AAA';", code);
        Assert.Contains("Account recAccount = new Account(Id = recAccountId);", code);
        Assert.Contains("recAccount.Name = 'Acme';", code);
        Assert.Contains("update recAccount;", code);
    }

    [Fact]
    public void Delete_GeneratesDeleteWithId()
    {
        var code = ApexDmlGenerator.Generate(new ApexDmlRequest("Contact", ApexDmlKinds.Delete, "003000000000001AAA", Array.Empty<ApexDmlFieldValue>()));

        Assert.Contains("Id recContactId = '003000000000001AAA';", code);
        Assert.Contains("delete new Contact(Id = recContactId);", code);
        Assert.DoesNotContain("update ", code);
    }

    [Fact]
    public void Generate_WithoutObject_ReturnsEmpty() =>
        Assert.Equal(string.Empty, ApexDmlGenerator.Generate(new ApexDmlRequest(" ", ApexDmlKinds.Insert, null, Array.Empty<ApexDmlFieldValue>())));

    // ---- 値の書式 ----

    [Theory]
    [InlineData("string", "Acme", "'Acme'")]
    [InlineData("string", "O'Brien", "'O\\'Brien'")]
    [InlineData("picklist", "Technology", "'Technology'")]
    [InlineData("multipicklist", "A;B", "new List<String>{ 'A', 'B' }")]
    [InlineData("multipicklist", "", "new List<String>()")]
    [InlineData("boolean", "TRUE", "true")]
    [InlineData("boolean", "", "false")]
    [InlineData("int", "42", "42")]
    [InlineData("int", "abc", "0")]
    [InlineData("double", "1.50", "1.5")]
    [InlineData("date", "2026-01-05", "Date.newInstance(2026, 1, 5)")]
    [InlineData("datetime", "2026-01-05 10:30:00", "Datetime.newInstance(2026, 1, 5, 10, 30, 0)")]
    [InlineData("time", "10:30:00", "Time.newInstance(10, 30, 0, 0)")]
    [InlineData("id", "001000000000001AAA", "'001000000000001AAA'")]
    [InlineData("date", "=Date.today()", "Date.today()")]
    [InlineData("string", "  =UserInfo.getUserId()  ", "UserInfo.getUserId()")]
    public void FormatValue_ConvertsByType(string type, string value, string expected) =>
        Assert.Equal(expected, ApexDmlGenerator.FormatValue(type, value));

    [Theory]
    [InlineData("boolean", "false")]
    [InlineData("int", "0")]
    [InlineData("date", "=Date.today()")]
    [InlineData("datetime", "=Datetime.now()")]
    [InlineData("string", "")]
    [InlineData("reference", "")]
    public void DefaultValue_MatchesType(string type, string expected) =>
        Assert.Equal(expected, ApexDmlGenerator.DefaultValue(type));

    [Fact]
    public void DefaultValue_PicklistUsesFirstActiveValue()
    {
        var values = new[]
        {
            new DataIoPicklistValue("Old", "Old", false),
            new DataIoPicklistValue("Active", "Active", true),
        };

        Assert.Equal("Active", ApexDmlGenerator.DefaultValue("picklist", values));
        Assert.Equal("Active", ApexDmlGenerator.DefaultValue("multipicklist", values));
    }

    [Theory]
    [InlineData("Account", "recAccount")]
    [InlineData("My_Obj__c", "recMyObj")]
    [InlineData("ns__Thing__c", "recNsThing")]
    [InlineData("Custom__mdt", "recCustom")]
    [InlineData("", "rec")]
    public void VariableName_AvoidsReservedWords(string objectName, string expected) =>
        Assert.Equal(expected, ApexDmlGenerator.VariableName(objectName));

    // ---- 候補レコードのクエリ ----

    [Fact]
    public void RecordQuery_BuildsNameQuery()
    {
        Assert.Equal(
            "SELECT Id, Name FROM Account ORDER BY Name LIMIT 30",
            ApexDmlRecordQuery.Build("Account", null));
        Assert.Equal(
            "SELECT Id, Name FROM Account WHERE Name LIKE '%acme%' ORDER BY Name LIMIT 10",
            ApexDmlRecordQuery.Build("Account", "acme", 10));
    }

    [Fact]
    public void RecordQuery_WithoutNameField_UsesId()
    {
        Assert.Equal(
            "SELECT Id FROM Log__c ORDER BY Id LIMIT 30",
            ApexDmlRecordQuery.Build("Log__c", null, 30, hasNameField: false));
        Assert.Equal(
            "SELECT Id FROM Log__c WHERE Id LIKE '001%' ORDER BY Id LIMIT 30",
            ApexDmlRecordQuery.Build("Log__c", "001", 30, hasNameField: false));
    }

    [Fact]
    public void RecordQuery_ParsesRecords()
    {
        using var document = JsonDocument.Parse(
            "{\"totalSize\":2,\"records\":[{\"Id\":\"001A\",\"Name\":\"Acme\"},{\"Id\":\"001B\",\"Name\":null}]}");

        var records = ApexDmlRecordQuery.Parse(document.RootElement);

        Assert.Equal(2, records.Count);
        Assert.Equal("Acme (001A)", records[0].Display);
        Assert.Equal("001B", records[1].Display);
    }

    [Fact]
    public void RecordQuery_ParseIgnoresInvalidPayload()
    {
        using var document = JsonDocument.Parse("{\"totalSize\":0}");
        Assert.Empty(ApexDmlRecordQuery.Parse(document.RootElement));
    }

    // ---- ViewModel ----

    [Fact]
    public async Task ViewModel_LoadsObjectsAndSelectsFirst()
    {
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        Assert.True(viewModel.Objects.Count > 0);
        Assert.NotNull(viewModel.SelectedObject);
        Assert.Equal("Account", viewModel.SelectedObject!.Name);
        Assert.Contains("insert recAccount;", viewModel.Preview);
    }

    [Fact]
    public async Task ViewModel_FilterAndSearchNarrowObjects()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        Assert.Equal(3, viewModel.Objects.Count);

        viewModel.SelectedFilter = viewModel.ObjectFilters.Single(f => f.Key == "custom");
        Assert.All(viewModel.Objects, o => Assert.True(o.Custom));

        viewModel.ObjectSearch = "zzz";
        Assert.Empty(viewModel.Objects);
        Assert.False(viewModel.HasObjects);
    }

    [Fact]
    public async Task ViewModel_InsertMarksRequiredFieldsAsSelected()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        var name = viewModel.Fields.Single(f => f.Name == "Name");
        Assert.True(name.IsRequired);
        Assert.True(name.IsSelected);
        Assert.False(name.IsEditable);

        var industry = viewModel.Fields.Single(f => f.Name == "Industry");
        Assert.False(industry.IsRequired);
        Assert.False(industry.IsSelected);
    }

    [Fact]
    public async Task ViewModel_DeleteHidesFieldsAndNeedsRecordId()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        viewModel.SelectedKind = viewModel.Kinds.Single(k => k.Key == ApexDmlKinds.Delete);

        Assert.False(viewModel.ShowsFields);
        Assert.True(viewModel.NeedsRecordId);
        Assert.Equal(UiText.T("Dml_ErrorNoRecordId"), viewModel.Validate());

        viewModel.RecordId = "001000000000001AAA";
        Assert.Null(viewModel.Validate());
        Assert.Contains("delete new Account(Id = recAccountId);", viewModel.Preview);
    }

    [Fact]
    public async Task ViewModel_PreviewFollowsValueEdits()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        var name = viewModel.Fields.Single(f => f.Name == "Name");
        name.Value = "Globex";

        Assert.Contains("recAccount.Name = 'Globex';", viewModel.Preview);
    }

    [Fact]
    public async Task ViewModel_UpdateRequiresAtLeastOneField()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.SelectedKind = viewModel.Kinds.Single(k => k.Key == ApexDmlKinds.Update);
        viewModel.RecordId = "001000000000001AAA";

        Assert.Equal(UiText.T("Dml_ErrorNoFields"), viewModel.Validate());

        viewModel.Fields.Single(f => f.Name == "Industry").IsSelected = true;
        Assert.Null(viewModel.Validate());
    }

    [Fact]
    public async Task ViewModel_InsertTreatsNameAsRequiredEvenWhenNillable()
    {
        // Account.Name は describe 上 nillable=true で返るが Salesforce では必須
        var viewModel = CreateViewModel(nillableName: true);
        await viewModel.InitializeAsync();

        var name = viewModel.Fields.Single(f => f.Name == "Name");
        Assert.True(name.IsRequired);
        Assert.True(name.IsSelected);
        Assert.Equal(UiText.T("Dml_ErrorValueRequiredFmt", name.Label), viewModel.Validate());
    }

    // ---- ヘルパー ----

    private static readonly DataIoObject[] Objects =
    {
        new("Account", "Account", true, true, true, true),
        new("My_Obj__c", "My Obj", true, true, true, true, Custom: true),
        new("ns__Pkg__c", "Pkg Obj", true, true, true, true, Custom: true, NamespacePrefix: "ns"),
    };

    private static readonly DataIoObjectDescribe AccountDescribe = new(
        "Account",
        "Account",
        new[]
        {
            new DataIoField("Name", "Name", "string", true, true, false, false, false, false, Array.Empty<string>()),
            new DataIoField("Industry", "Industry", "picklist", true, true, true, false, false, false, Array.Empty<string>(), PicklistValues: new[]
            {
                new DataIoPicklistValue("Technology", "Technology"),
                new DataIoPicklistValue("Finance", "Finance"),
            }),
            new DataIoField("DaysOpen__c", "Days Open", "double", true, true, true, false, true, true, Array.Empty<string>()),
        });

    private static ApexDmlGeneratorViewModel CreateViewModel(bool nillableName = false)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var paths = AppPaths.Resolve(dataRootOverride: sandbox, baseDirectory: sandbox, appDataDirectory: sandbox);
        var log = new AppLog(paths);
        var describe = nillableName
            ? AccountDescribe with { Fields = new[] { AccountDescribe.Fields[0] with { Nillable = true } } }
            : AccountDescribe;
        var describes = new SObjectDescribeService(
            log,
            null,
            (_, _) => Task.FromResult<IReadOnlyList<DataIoObject>>(Objects),
            (_, name, _) => Task.FromResult(name == "Account"
                ? describe
                : new DataIoObjectDescribe(name, name, Array.Empty<DataIoField>())));

        return new ApexDmlGeneratorViewModel("org", describes, new FakeRecordService(), log);
    }

    private sealed class FakeRecordService : IApexDmlRecordService
    {
        public Task<IReadOnlyList<ApexDmlRecord>> SearchAsync(string targetOrg, string sobject, string? search, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApexDmlRecord>>(new[] { new ApexDmlRecord("001000000000001AAA", "Acme") });
    }
}
