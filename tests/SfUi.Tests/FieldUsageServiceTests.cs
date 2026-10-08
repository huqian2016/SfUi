using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>FieldUsageService のスキャン純関数（行スキャン / JSON 走査 / 一致境界）のテスト。</summary>
public class FieldUsageServiceTests
{
    [Fact]
    public void ScanText_MatchesExactTokensOnly()
    {
        const string apex = """
public class Demo {
    void Run() {
        Account a = new Account();
        a.Name = 'x';
        a.MyField__c = 'y';
        System.debug(a.MyField__cExtra);
        String s = 'MyField__c';
    }
}
""";
        var hits = FieldUsageService.ScanText(apex, "MyField__c", "Demo", FieldUsageSourceKind.ApexClass);

        // 一致するのは「a.MyField__c = 'y';」の行のみ（MyField__cExtra は不一致、文字列リテラルは一致）
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Equal(FieldUsageSourceKind.ApexClass, h.SourceKind));
        Assert.Contains(hits, h => h.LineNumber == 5 && h.Excerpt.Contains("a.MyField__c"));
        Assert.Contains(hits, h => h.LineNumber == 7 && h.Excerpt.Contains("'MyField__c'"));
    }

    [Fact]
    public void ScanText_RespectsCap()
    {
        var text = string.Join('\n', Enumerable.Range(0, 30).Select(i => $"line {i} has Name here"));
        var hits = FieldUsageService.ScanText(text, "Name", "Demo", FieldUsageSourceKind.ApexClass);

        Assert.Equal(FieldUsageService.MaxHitsPerComponent, hits.Count);
        Assert.Equal(1, hits[0].LineNumber);
    }

    [Fact]
    public void ScanMetadata_FlowJson_FindsFieldReferencesWithPath()
    {
        const string flowJson = """
{"recordUpdates":[{"inputAssignments":[{"field":"Industry"},{"field":"MyField__c","value":{"stringValue":"x"}}]}],"decisions":[{"defaultConnectorLabel":"No"}]}
""";
        var hits = FieldUsageService.ScanMetadata(flowJson, "MyField__c", "MyFlow", FieldUsageSourceKind.Flow);

        var hit = Assert.Single(hits);
        Assert.Equal("recordUpdates[0].inputAssignments[1].field", hit.Path);
        Assert.Equal("MyField__c", hit.Excerpt);

        var industry = FieldUsageService.ScanMetadata(flowJson, "Industry", "MyFlow", FieldUsageSourceKind.Flow);
        Assert.Single(industry);
        Assert.Equal("recordUpdates[0].inputAssignments[0].field", industry[0].Path);
    }

    [Fact]
    public void ScanMetadata_LayoutJson_FindsLayoutItems()
    {
        const string layoutJson = """
{"layoutSections":[{"label":"A","layoutColumns":[{"layoutItems":[{"field":"Name"},{"field":"OwnerId"}]}]}]}
""";
        var hits = FieldUsageService.ScanMetadata(layoutJson, "Name", "Account Layout", FieldUsageSourceKind.Layout);

        var hit = Assert.Single(hits);
        Assert.Equal("layoutSections[0].layoutColumns[0].layoutItems[0].field", hit.Path);
    }

    [Fact]
    public void ScanMetadata_XmlFallback_ScansText()
    {
        const string xml = "<ValidationRule><fullName>R1</fullName><errorConditionFormula>AND(Name = \"x\", Amount &gt; 0)</errorConditionFormula></ValidationRule>";
        var hits = FieldUsageService.ScanMetadata(xml, "Name", "R1", FieldUsageSourceKind.ValidationRule);

        var hit = Assert.Single(hits);
        Assert.Contains("Name", hit.Excerpt);
        Assert.Null(hit.Path);
    }

    [Fact]
    public void ScanMetadata_DoesNotMatchSubstringsOrSimilarNames()
    {
        const string flowJson = """
{"a":"MyField__cExtra","b":"NotMyField__c","c":"MyField__cx"}
""";
        var hits = FieldUsageService.ScanMetadata(flowJson, "MyField__c", "Flow", FieldUsageSourceKind.Flow);
        Assert.Empty(hits);

        // 文字列値は含まないがキーにだけ現れる場合はヒットしない
        Assert.Empty(FieldUsageService.ScanMetadata("{\"MyField__c\":null}", "MyField__c", "Flow", FieldUsageSourceKind.Flow));
    }

    [Fact]
    public void BuildFieldRegex_UsesWordBoundaries()
    {
        var regex = FieldUsageService.BuildFieldRegex("Account.Name");
        Assert.Matches(regex, "obj.Account.Name");
        Assert.Matches(regex, "Account.Name");
        Assert.DoesNotMatch(regex, "MyAccount.Name");
        Assert.DoesNotMatch(regex, "Account.Name2");
    }

    [Fact]
    public void ScanText_SkipCommentLines_IgnoresApexComments()
    {
        const string apex = """
public class Demo {
    // uses Name in a comment
    /* block comment about Name
     * still a comment with Name
     */
    void Run() {
        Account a = new Account();
        a.Name = 'x';
    }
}
""";
        var withComments = FieldUsageService.ScanText(apex, "Name", "Demo", FieldUsageSourceKind.ApexClass);
        var withoutComments = FieldUsageService.ScanText(apex, "Name", "Demo", FieldUsageSourceKind.ApexClass, skipCommentLines: true);

        Assert.True(withoutComments.Count < withComments.Count);
        Assert.DoesNotContain(withoutComments, h => h.Excerpt.StartsWith("//"));
        Assert.DoesNotContain(withoutComments, h => h.Excerpt.StartsWith("*"));
        Assert.DoesNotContain(withoutComments, h => h.Excerpt.StartsWith("/*"));
        Assert.Contains(withoutComments, h => h.Excerpt.Contains("a.Name = 'x'"));
    }

    [Fact]
    public void ScanText_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(FieldUsageService.ScanText("", "Name", "X", FieldUsageSourceKind.ApexClass));
        Assert.Empty(FieldUsageService.ScanMetadata("   ", "Name", "X", FieldUsageSourceKind.Flow));
    }

    [Fact]
    public void ScanMetadata_InlineStringsInsideLargerText_Match()
    {
        // フローのフォーミュラ/文字列内に項目名が埋まっているケース
        const string flowJson = """
{"formulas":[{"expression":"{!MyField__c} + 1"}]}
""";
        var hits = FieldUsageService.ScanMetadata(flowJson, "MyField__c", "Flow", FieldUsageSourceKind.Flow);
        var hit = Assert.Single(hits);
        Assert.Equal("formulas[0].expression", hit.Path);
    }
}
