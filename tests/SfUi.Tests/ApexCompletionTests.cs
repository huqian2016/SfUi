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
    public void Parser_DetectsVariableMemberChain()
    {
        var simpleText = "Account a = new Account();\na.Ow";
        var simple = ApexCompletionParser.Parse(simpleText, simpleText.Length);
        Assert.Equal(ApexCompletionKind.Members, simple.Kind);
        Assert.Equal("a", simple.Root);
        Assert.Equal("Ow", simple.Prefix);
        Assert.Null(simple.Path);

        var chainText = "Account a = new Account();\na.Owner.Na";
        var chain = ApexCompletionParser.Parse(chainText, chainText.Length);
        Assert.Equal(ApexCompletionKind.Members, chain.Kind);
        Assert.Equal("a", chain.Root);
        Assert.Equal(new[] { "Owner" }, chain.Path);
        Assert.Equal("Na", chain.Prefix);
    }

    [Fact]
    public void Parser_DetectsNewObjectContext()
    {
        var text = "Account a = new Acc";
        var context = ApexCompletionParser.Parse(text, text.Length);
        Assert.Equal(ApexCompletionKind.Objects, context.Kind);
        Assert.Equal("Acc", context.Prefix);
        Assert.Equal(16, context.SegmentStart);

        var emptyText = "Account a = new ";
        var empty = ApexCompletionParser.Parse(emptyText, emptyText.Length);
        Assert.Equal(ApexCompletionKind.Objects, empty.Kind);
        Assert.Equal(string.Empty, empty.Prefix);
    }

    [Fact]
    public void Parser_DetectsGenericAngleObjectContext()
    {
        var list = ApexCompletionParser.Parse("List<Con", 8);
        Assert.Equal(ApexCompletionKind.Objects, list.Kind);
        Assert.Equal("Con", list.Prefix);

        var map = ApexCompletionParser.Parse("Map<Id, Con", 11);
        Assert.Equal(ApexCompletionKind.Objects, map.Kind);
        Assert.Equal("Con", map.Prefix);
    }

    [Fact]
    public void Parser_DetectsSystemLabelPath()
    {
        var text = "System.debug(System.Label.Wel";
        var context = ApexCompletionParser.Parse(text, text.Length);

        Assert.Equal(ApexCompletionKind.Members, context.Kind);
        Assert.Equal("System", context.Root);
        Assert.Equal(new[] { "Label" }, context.Path);
        Assert.Equal("Wel", context.Prefix);
    }

    [Fact]
    public void Parser_DetectsDmlVariableContext()
    {
        var context = ApexCompletionParser.Parse("insert acc", 10);
        Assert.Equal(ApexCompletionKind.Variables, context.Kind);
        Assert.Equal("acc", context.Prefix);

        var empty = ApexCompletionParser.Parse("update ", 7);
        Assert.Equal(ApexCompletionKind.Variables, empty.Kind);
        Assert.Equal(string.Empty, empty.Prefix);
    }

    [Fact]
    public void Parser_ScanDeclarations_FindsVariablesAndSkipsKeywords()
    {
        var text = "Account a = new Account();\n"
                   + "List<Contact> cs = null;\n"
                   + "String s = 'Account fake = new Account();';\n"
                   + "insert cs;\n"
                   + "for (Contact c : cs) { }";
        var map = ApexCompletionParser.ScanDeclarations(text);

        Assert.Equal("Account", map["a"]);
        Assert.Equal("List<Contact>", map["cs"]);
        Assert.Equal("String", map["s"]);
        Assert.Equal("Contact", map["c"]);
        Assert.False(map.ContainsKey("fake"));
        Assert.False(map.ContainsKey("insert"));
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

    // ---- Phase 2: sObject / 変数 / コレクション ----

    [Fact]
    public void Engine_TryParseCollectionType()
    {
        Assert.True(ApexCompletionEngine.TryParseCollectionType("List<Account>", out var listKind, out var listElement));
        Assert.Equal("List", listKind);
        Assert.Equal("Account", listElement);

        Assert.True(ApexCompletionEngine.TryParseCollectionType("Map<Id, Account>", out var mapKind, out var mapElement));
        Assert.Equal("Map", mapKind);
        Assert.Equal("Account", mapElement);

        Assert.False(ApexCompletionEngine.TryParseCollectionType("Account", out _, out _));
    }

    [Fact]
    public void Engine_HasStaticClassAndSObjectMethods()
    {
        Assert.True(ApexCompletionEngine.HasStaticClass("system"));
        Assert.True(ApexCompletionEngine.HasStaticClass("Database"));
        Assert.False(ApexCompletionEngine.HasStaticClass("Account"));

        Assert.Equal("addError()", ApexCompletionEngine.SObjectMethods("ad").Single().Text);
    }

    [Fact]
    public void Engine_CollectionMethods_FilterByPrefix()
    {
        Assert.Equal(
            new[] { "add()", "addAll()" },
            ApexCompletionEngine.CollectionMethods("List", "ad").Select(i => i.Text));

        Assert.Equal(
            new[] { "put()", "putAll()" },
            ApexCompletionEngine.CollectionMethods("Map", "pu").Select(i => i.Text));

        Assert.Empty(ApexCompletionEngine.CollectionMethods("Set", "zzz"));
    }

    [Fact]
    public void Engine_NameItems_FiltersByPrefix()
    {
        var names = new[] { "AccountHelper", "OrderService", "RetryQueueJob" };
        var items = ApexCompletionEngine.NameItems(names, "ord", "Apex class");

        Assert.Equal("OrderService", items.Single().Text);
        Assert.Equal("Apex class", items.Single().Description);
    }
}
