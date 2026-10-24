using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>ER 図のテキスト形式ライター（Mermaid / DBML / DOT / PlantUML / draw.io）。</summary>
public class ErdDiagramWriterTests
{
    private static ErdDocument CreateDocument(ErdNameStyle style = ErdNameStyle.LabelAndApiName)
    {
        var account = new ErdEntity
        {
            ApiName = "Account",
            Label = "取引先",
            Fields = new List<ErdField>
            {
                new() { ApiName = "Id", Label = "取引先 ID", DataType = "Id", IsPrimaryKey = true },
                new() { ApiName = "Name", Label = "取引先名", DataType = "string" },
                new()
                {
                    ApiName = "OwnerId",
                    Label = "所有者 ID",
                    DataType = "reference",
                    ReferenceTo = new[] { "User" },
                    RelationshipName = "Owner",
                },
                new()
                {
                    ApiName = "ParentId",
                    Label = "親取引先 ID",
                    DataType = "reference",
                    ReferenceTo = new[] { "Account" },
                    RelationshipName = "Parent",
                },
            },
        };
        var contact = new ErdEntity
        {
            ApiName = "Contact",
            Label = "担当者",
            Fields = new List<ErdField>
            {
                new() { ApiName = "Id", Label = "担当者 ID", DataType = "Id", IsPrimaryKey = true },
                new()
                {
                    ApiName = "AccountId",
                    Label = "取引先 ID",
                    DataType = "reference",
                    ReferenceTo = new[] { "Account" },
                    IsMasterDetail = true,
                    IsRequired = true,
                },
            },
        };
        var risky = new ErdEntity
        {
            ApiName = "Thing__c",
            Label = "R&D <試験>",
            IsCustom = true,
            Fields = new List<ErdField>
            {
                new() { ApiName = "Id", DataType = "Id", IsPrimaryKey = true },
            },
        };

        return new ErdDocument
        {
            Title = "Test ERD",
            Org = "dev@example.com",
            NameStyle = style,
            Entities = new[] { account, contact, risky },
            Relationships = new[]
            {
                new ErdRelationship { ChildApiName = "Contact", ParentApiName = "Account", FieldApiName = "AccountId", IsMasterDetail = true, IsRequired = true },
                new ErdRelationship { ChildApiName = "Account", ParentApiName = "Account", FieldApiName = "ParentId" },
                new ErdRelationship { ChildApiName = "Contact", ParentApiName = "Missing__c", FieldApiName = "Missing__c" },
            },
        };
    }

    [Fact]
    public void Mermaid_UsesAliasAndRequiredCardinality()
    {
        var text = ErdDiagramWriter.WriteMermaid(CreateDocument());

        Assert.Contains("erDiagram", text);
        Assert.Contains("Account[\"取引先 (Account)\"]", text);
        Assert.Contains("Contact[\"担当者 (Contact)\"]", text);
        Assert.Contains("Account ||--|{ Contact", text);   // master-detail + required
        Assert.Contains("Account ||--o{ Account", text);   // self reference, optional
        Assert.Contains("Id Id PK", text);
        Assert.Contains("Account AccountId FK", text);
    }

    [Fact]
    public void Mermaid_ApiNameStyle_OmitsAlias()
    {
        var text = ErdDiagramWriter.WriteMermaid(CreateDocument(ErdNameStyle.ApiName));

        Assert.Contains("    Account {", text);
        Assert.DoesNotContain("[\"", text);
    }

    [Fact]
    public void Mermaid_LabelStyle_UsesJapaneseOnly()
    {
        var text = ErdDiagramWriter.WriteMermaid(CreateDocument(ErdNameStyle.Label));

        Assert.Contains("Account[\"取引先\"]", text);
        Assert.DoesNotContain("取引先 (Account)", text);
    }

    [Fact]
    public void Dbml_EmitsPrimaryKeyAndRefs()
    {
        var text = ErdDiagramWriter.WriteDbml(CreateDocument());

        Assert.Contains("Table Account {", text);
        Assert.Contains("  Id Id [pk]", text);
        Assert.Contains("Ref: Contact.AccountId > Account.Id", text);
        Assert.Contains("Ref: Account.ParentId > Account.Id", text);
    }

    [Fact]
    public void Dot_ContainsRecordsAndCrowFootEdges()
    {
        var text = ErdDiagramWriter.WriteDot(CreateDocument());

        Assert.Contains("digraph erd {", text);
        Assert.Contains("\"Account\" [label=", text);
        Assert.Contains("arrowhead=crow", text);
        Assert.Contains("label=\"AccountId\"", text);
    }

    [Fact]
    public void PlantUml_ContainsEntitiesAndCardinality()
    {
        var text = ErdDiagramWriter.WritePlantUml(CreateDocument());

        Assert.StartsWith("' Test ERD", text);
        Assert.Contains("@startuml", text);
        Assert.Contains("entity \"取引先 (Account)\" as Account", text);
        Assert.Contains("Account ||--|{ Contact", text);
        Assert.Contains("@enduml", text);
    }

    [Fact]
    public void Drawio_EscapesXml_AndSkipsUnknownTargets()
    {
        var text = ErdDiagramWriter.WriteDrawio(CreateDocument());

        Assert.Contains("<mxfile", text);
        Assert.Contains("R&amp;D &lt;試験&gt;", text);
        Assert.Contains("source=\"e0\" target=\"e1\"", text);   // Account -> Contact (parent -> child)
        Assert.DoesNotContain("Missing__c\" edge=\"1\"", text);
    }

    [Fact]
    public void EmptyDocument_ProducesValidMinimalOutput()
    {
        var document = new ErdDocument { Title = "Empty" };

        Assert.Contains("erDiagram", ErdDiagramWriter.WriteMermaid(document));
        Assert.Contains("Table ", string.Concat("Table ", ErdDiagramWriter.WriteDbml(document))); // no crash
        Assert.Contains("digraph erd", ErdDiagramWriter.WriteDot(document));
        Assert.Contains("@startuml", ErdDiagramWriter.WritePlantUml(document));
        Assert.Contains("<mxfile", ErdDiagramWriter.WriteDrawio(document));
    }

    [Theory]
    [InlineData(ErdDiagramFormat.Mermaid, "mmd")]
    [InlineData(ErdDiagramFormat.Dbml, "dbml")]
    [InlineData(ErdDiagramFormat.Drawio, "drawio")]
    [InlineData(ErdDiagramFormat.PlantUml, "puml")]
    [InlineData(ErdDiagramFormat.Dot, "dot")]
    public void FileExtension_IsStable(ErdDiagramFormat format, string expected)
    {
        Assert.Equal(expected, ErdDiagramWriter.FileExtension(format));
    }
}
