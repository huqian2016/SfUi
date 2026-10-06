using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class ApexCompletionTests
{
    // ---- コンテキスト解析 ----

    [Fact]
    public void Parser_DetectsSnippetWord()
    {
        var context = ApexCompletionParser.Parse("Syst", 4);

        Assert.Equal(ApexCompletionKind.Snippets, context.Kind);
        Assert.Equal("Syst", context.Prefix);
        Assert.Equal(0, context.SegmentStart);
    }

    [Fact]
    public void Parser_EmptyWordIsNone()
    {
        var context = ApexCompletionParser.Parse("Integer i = ", 12);

        Assert.Equal(ApexCompletionKind.None, context.Kind);
    }

    [Fact]
    public void Parser_DetectsStaticMemberAccess()
    {
        var context = ApexCompletionParser.Parse("System.de", 9);

        Assert.Equal(ApexCompletionKind.Members, context.Kind);
        Assert.Equal("System", context.Root);
        Assert.Equal("de", context.Prefix);
        Assert.Equal(7, context.SegmentStart);
    }

    [Fact]
    public void Parser_IgnoresDottedChains()
    {
        var context = ApexCompletionParser.Parse("record.Owner.Na", 15);

        Assert.Equal(ApexCompletionKind.None, context.Kind);
    }

    [Fact]
    public void Parser_DetectsInlineSoql()
    {
        var text = "List<Account> a = [SELECT Id FROM Acc";
        var context = ApexCompletionParser.Parse(text, text.Length);

        Assert.Equal(ApexCompletionKind.Soql, context.Kind);
        Assert.Equal("Acc", context.Prefix);
        Assert.NotNull(context.Soql);
        Assert.Equal(SoqlClause.From, context.Soql!.Clause);
        Assert.Equal(new[] { "Acc" }, context.Soql.FromObjects);
    }

    [Fact]
    public void Parser_DetectsInlineSoqlFields()
    {
        var text = "System.debug([SELECT Acc";
        var context = ApexCompletionParser.Parse(text, text.Length);

        Assert.Equal(ApexCompletionKind.Soql, context.Kind);
        Assert.Equal("Acc", context.Prefix);
        Assert.Equal(SoqlClause.Select, context.Soql!.Clause);
        // 挿入は [ の中の語だけを置き換える
        Assert.Equal(text.Length - 3, context.SegmentStart);
    }

    [Fact]
    public void Parser_ClosedBracketIsNotSoql()
    {
        var text = "[SELECT Id FROM Account] Sys";
        var context = ApexCompletionParser.Parse(text, text.Length);

        Assert.Equal(ApexCompletionKind.Snippets, context.Kind);
        Assert.Equal("Sys", context.Prefix);
    }

    [Fact]
    public void Parser_SoqlStringContainingBracketStillDetected()
    {
        var text = "[SELECT Id FROM Account WHERE Name = 'a]b' AND Nam";
        var context = ApexCompletionParser.Parse(text, text.Length);

        Assert.Equal(ApexCompletionKind.Soql, context.Kind);
        Assert.Equal("Nam", context.Prefix);
        Assert.Equal(SoqlClause.Where, context.Soql!.Clause);
    }

    [Fact]
    public void Parser_SuppressesInsideStringAndComments()
    {
        var inString = "System.debug('Syst";
        Assert.Equal(ApexCompletionKind.None, ApexCompletionParser.Parse(inString, inString.Length).Kind);

        var inLineComment = "Integer i = 1; // Syst";
        Assert.Equal(ApexCompletionKind.None, ApexCompletionParser.Parse(inLineComment, inLineComment.Length).Kind);

        var inBlockComment = "/* Syst";
        Assert.Equal(ApexCompletionKind.None, ApexCompletionParser.Parse(inBlockComment, inBlockComment.Length).Kind);
    }

    // ---- 候補生成 ----

    [Fact]
    public void Engine_Snippets_ContainsTemplatesWithCursor()
    {
        var debug = ApexCompletionEngine.Snippets("system").First(i => i.Text == "System.debug();");
        Assert.Equal(-2, debug.CaretOffsetDelta);   // () の中にカーソル

        var ifItem = ApexCompletionEngine.Snippets("if").First(i => i.Text.StartsWith("if () {", StringComparison.Ordinal));
        Assert.Equal(4 - ifItem.Text.Length, ifItem.CaretOffsetDelta);

        Assert.Contains(ApexCompletionEngine.Snippets("lis"), i => i.Text == "List<>" && i.CaretOffsetDelta == -1);
    }

    [Fact]
    public void Engine_Snippets_ContainsTypesKeywordsAndClasses()
    {
        Assert.Contains(ApexCompletionEngine.Snippets("str"), i => i.Text == "String");
        Assert.Contains(ApexCompletionEngine.Snippets("el"), i => i.Text == "else");
        Assert.Contains(ApexCompletionEngine.Snippets("sys"), i => i.Text == "System");
        Assert.Empty(ApexCompletionEngine.Snippets("zzz"));
    }

    [Fact]
    public void Engine_StaticMembers_FiltersByPrefix()
    {
        var system = ApexCompletionEngine.StaticMembers("System", "de");
        Assert.Equal("debug()", system.Single().Text);

        var database = ApexCompletionEngine.StaticMembers("Database", "ins");
        Assert.Equal(new[] { "insert()" }, database.Select(i => i.Text));

        Assert.Empty(ApexCompletionEngine.StaticMembers("Nope", string.Empty));
    }

    [Fact]
    public void Engine_StaticMembers_IsCaseInsensitive()
    {
        Assert.Equal("debug()", ApexCompletionEngine.StaticMembers("system", "DE").Single().Text);
    }
}
