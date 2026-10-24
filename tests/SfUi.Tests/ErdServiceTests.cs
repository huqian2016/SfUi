using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>ERD のメタデータ解釈（EntityDefinition / FieldDefinition）とモデル組み立て。</summary>
public class ErdServiceTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void ParseObjects_ReadsLabelAndCustomSetting()
    {
        var objects = ErdService.ParseObjects(new[]
        {
            Json("""{"QualifiedApiName":"Account","Label":"取引先"}"""),
            Json("""{"QualifiedApiName":"MyObj__c","Label":"カスタム"}"""),
            Json("""{"QualifiedApiName":"Config__c","Label":"設定","IsCustomSetting":true}"""),
        });

        Assert.Equal(3, objects.Count);
        Assert.Equal("取引先", objects[0].Label);
        Assert.False(objects[0].IsCustom);
        Assert.True(objects[1].IsCustom);
        Assert.True(objects[2].IsCustomSetting);
        Assert.True(objects[2].IsSystemObject);
    }

    [Fact]
    public void ParseFieldReferences_HandlesPolymorphicAndNestedEntity()
    {
        var references = ErdService.ParseFieldReferences(new[]
        {
            Json("""{"EntityDefinition":{"QualifiedApiName":"Task","Label":"ToDo"},"QualifiedApiName":"WhoId","Label":"名簿 ID","DataType":"Lookup","ReferenceTo":["Contact","Lead"],"IsNillable":true}"""),
            Json("""{"EntityDefinition":"Contact","QualifiedApiName":"AccountId","Label":"取引先 ID","DataType":"Master-Detail","ReferenceTo":["Account"],"IsNillable":false}"""),
            Json("""{"EntityDefinition":"Contact","QualifiedApiName":"Name","DataType":"string"}"""),
        });

        Assert.Equal(2, references.Count);   // Name has no ReferenceTo
        Assert.Equal(new[] { "Contact", "Lead" }, references[0].ReferenceTo);
        Assert.True(references[0].IsNillable);
        Assert.True(references[1].IsMasterDetail);
        Assert.False(references[1].IsNillable);
    }

    [Fact]
    public void Build_AddsIdPrimaryKeyAndRelationships()
    {
        var document = ErdService.Build(
            new[]
            {
                new ErdObjectInfo("Account", "取引先", false, false),
                new ErdObjectInfo("Contact", "担当者", false, false),
            },
            new[]
            {
                new ErdFieldReference("Contact", "AccountId", "取引先 ID", "Master-Detail", new[] { "Account" }, false),
                new ErdFieldReference("Account", "ParentId", "親取引先 ID", "Lookup", new[] { "Account" }, true),
            },
            new ErdBuildOptions(),
            "dev@example.com");

        Assert.Equal(2, document.Entities.Count);
        var account = Assert.Single(document.Entities, entity => entity.ApiName == "Account");
        Assert.Equal("Id", account.Fields[0].ApiName);
        Assert.True(account.Fields[0].IsPrimaryKey);
        Assert.Contains(account.Fields, field => field.ApiName == "ParentId" && field.IsLookup);

        Assert.Equal(2, document.Relationships.Count);
        var contact = Assert.Single(document.Relationships, relationship => relationship.ChildApiName == "Contact");
        Assert.True(contact.IsMasterDetail);
        Assert.True(contact.IsRequired);
    }

    [Fact]
    public void Build_ExcludesSystemObjectsAndOutOfScopeTargets()
    {
        var document = ErdService.Build(
            new[]
            {
                new ErdObjectInfo("Account", "取引先", false, false),
                new ErdObjectInfo("History__history", "履歴", true, false),
                new ErdObjectInfo("Setting__c", "設定", true, true),
                new ErdObjectInfo("Event__e", "イベント", true, false),
            },
            new[]
            {
                new ErdFieldReference("Account", "OwnerId", "所有者", "Lookup", new[] { "User" }, true),
            },
            new ErdBuildOptions(),
            null);

        Assert.Single(document.Entities);
        Assert.Empty(document.Relationships);   // User is out of scope
    }

    [Fact]
    public void Build_ExcludesStandardObjects_WhenDisabled()
    {
        var document = ErdService.Build(
            new[]
            {
                new ErdObjectInfo("Account", "取引先", false, false),
                new ErdObjectInfo("MyObj__c", "カスタム", true, false),
            },
            Array.Empty<ErdFieldReference>(),
            new ErdBuildOptions { IncludeStandardObjects = false },
            null);

        var entity = Assert.Single(document.Entities);
        Assert.Equal("MyObj__c", entity.ApiName);
    }

    [Fact]
    public void Build_DeduplicatesAndRespectsMaxObjects()
    {
        var objects = Enumerable.Range(1, 5)
            .Select(index => new ErdObjectInfo("Obj" + index + "__c", "オブジェクト" + index, true, false))
            .ToArray();
        var references = new[]
        {
            new ErdFieldReference("Obj1__c", "Parent__c", "親", "Lookup", new[] { "Obj2__c" }, true),
            new ErdFieldReference("Obj1__c", "Parent__c", "親", "Lookup", new[] { "Obj2__c" }, true),
        };

        var document = ErdService.Build(objects, references, new ErdBuildOptions { MaxObjects = 3 }, null);

        Assert.Equal(3, document.Entities.Count);
        Assert.Single(document.Relationships);
        Assert.Equal("Obj1__c", document.Relationships[0].ChildApiName);
    }

    [Theory]
    [InlineData("Account", false, false)]
    [InlineData("Config__c", true, true)]
    [InlineData("Foo__mdt", false, true)]
    [InlineData("Bar__e", false, true)]
    [InlineData("Baz__ChangeEvent", false, true)]
    [InlineData("Contact", false, false)]
    public void IsSystemObject_DetectsPlatformTypes(string name, bool customSetting, bool expected)
    {
        Assert.Equal(expected, ErdService.IsSystemObject(name, customSetting));
    }
}
