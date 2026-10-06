using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>グローバルな言語状態に依存する文字列を検証するため、言語切替テストと直列実行する。</summary>
[Collection("Localization")]
public class SoqlCompletionTests
{
    private static DataIoObjectDescribe ContactDescribe() => new(
        "Contact",
        "Contact",
        new[]
        {
            new DataIoField("Id", "Contact ID", "id", false, false, false, false, false, false, Array.Empty<string>()),
            new DataIoField("AccountId", "Account ID", "reference", true, false, true, false, false, false, new[] { "Account" }, "Account"),
            new DataIoField("OwnerId", "Owner ID", "reference", true, false, true, false, false, false, new[] { "User" }, "Owner"),
            new DataIoField("Name", "Name", "string", true, true, false, false, false, false, Array.Empty<string>()),
        });

    // ---- コンテキスト解析 ----

    [Fact]
    public void Parser_DetectsClausesAndPrefix()
    {
        var select = SoqlCompletionParser.Parse("SELECT Acc", 10);
        Assert.Equal(SoqlClause.Select, select.Clause);
        Assert.Equal("Acc", select.Prefix);
        Assert.Equal(7, select.WordStart);
        Assert.True(select.IsFieldClause);
        Assert.True(select.IncludeFunctions);

        var where = SoqlCompletionParser.Parse("SELECT Id FROM Contact WHERE Nam", 32);
        Assert.Equal(SoqlClause.Where, where.Clause);
        Assert.Equal("Nam", where.Prefix);
        Assert.True(where.IsFieldClause);
        Assert.False(where.IncludeFunctions);

        var group = SoqlCompletionParser.Parse("SELECT Id FROM Contact GROUP BY Nam", 35);
        Assert.Equal(SoqlClause.GroupBy, group.Clause);
        Assert.Equal("Nam", group.Prefix);

        var order = SoqlCompletionParser.Parse("SELECT Id FROM Contact ORDER BY Nam", 35);
        Assert.Equal(SoqlClause.OrderBy, order.Clause);
        Assert.Equal("Nam", order.Prefix);
    }

    [Fact]
    public void Parser_SplitsPathAndPrefix()
    {
        var empty = SoqlCompletionParser.Parse("SELECT Account. FROM Contact", 15);
        Assert.Equal(SoqlClause.Select, empty.Clause);
        Assert.Equal(string.Empty, empty.Prefix);
        Assert.Equal(new[] { "Account" }, empty.Path);

        var partial = SoqlCompletionParser.Parse("SELECT Account.Ow FROM Contact", 17);
        Assert.Equal("Ow", partial.Prefix);
        Assert.Equal(new[] { "Account" }, partial.Path);

        var deep = SoqlCompletionParser.Parse("SELECT Account.Owner.Na FROM Contact", 23);
        Assert.Equal("Na", deep.Prefix);
        Assert.Equal(new[] { "Account", "Owner" }, deep.Path);
    }

    [Fact]
    public void Parser_DetectsFromClauseAndCollectsObjectsAndAliases()
    {
        var typing = SoqlCompletionParser.Parse("SELECT Id FROM Cont", 19);
        Assert.Equal(SoqlClause.From, typing.Clause);
        Assert.Equal("Cont", typing.Prefix);
        Assert.Equal(new[] { "Cont" }, typing.FromObjects);

        var alias = SoqlCompletionParser.Parse("SELECT c.Nam FROM Contact c WHERE c.Nam", 39);
        Assert.Equal(SoqlClause.Where, alias.Clause);
        Assert.Equal(new[] { "Contact" }, alias.FromObjects);
        Assert.Equal("Contact", alias.Aliases["c"]);
        Assert.Equal(new[] { "c" }, alias.Path);
        Assert.Equal("Nam", alias.Prefix);

        var multi = SoqlCompletionParser.Parse("SELECT Id FROM Contact, Account", 30);
        Assert.Equal(new[] { "Contact", "Account" }, multi.FromObjects);
    }

    [Fact]
    public void Parser_FindsFromObjectEvenWhenCaretIsBeforeFrom()
    {
        // 「SELECT  FROM Contact」でカーソルが SELECT の後ろ（スペース入力直後）
        var context = SoqlCompletionParser.Parse("SELECT  FROM Contact", 7);

        Assert.Equal(SoqlClause.Select, context.Clause);
        Assert.Equal(string.Empty, context.Prefix);
        Assert.Equal(7, context.WordStart);
        Assert.Equal(new[] { "Contact" }, context.FromObjects);
        Assert.True(context.IsFieldClause);
    }

    [Fact]
    public void Parser_DoesNotCompleteInsideStringOrComment()
    {
        var inString = SoqlCompletionParser.Parse("SELECT Id FROM Contact WHERE Name = 'Acc", 40);
        Assert.True(inString.InsideString);
        Assert.Equal(SoqlClause.None, inString.Clause);

        var inComment = SoqlCompletionParser.Parse("SELECT Id FROM Contact -- Acc", 30);
        Assert.True(inComment.InsideString);
        Assert.Equal(SoqlClause.None, inComment.Clause);

        var afterString = SoqlCompletionParser.Parse("SELECT Id FROM Contact WHERE Name = 'x' AND Nam", 50);
        Assert.False(afterString.InsideString);
        Assert.Equal(SoqlClause.Where, afterString.Clause);
        Assert.Equal("Nam", afterString.Prefix);
    }

    // ---- 候補生成 ----

    [Fact]
    public void Engine_ObjectItems_FiltersByPrefixAndQueryable()
    {
        var objects = new[]
        {
            new DataIoObject("Account", "Account", true, true, true, true),
            new DataIoObject("AccountHistory", "Account History", false, false, false, true),
            new DataIoObject("Contact", "Contact", true, true, true, true),
        };

        var items = SoqlCompletionEngine.ObjectItems(objects, "acc");
        Assert.Equal(new[] { "Account" }, items.Select(i => i.Text));

        var all = SoqlCompletionEngine.ObjectItems(objects, string.Empty);
        Assert.Equal(new[] { "Account", "Contact" }, all.Select(i => i.Text));
    }

    [Fact]
    public void Engine_FieldItems_IncludesRelationshipAndFunctions()
    {
        var describe = ContactDescribe();

        var filtered = SoqlCompletionEngine.FieldItems(describe, "acc", includeFunctions: true);
        Assert.Equal(new[] { "AccountId", "Account." }, filtered.Select(i => i.Text));
        Assert.All(filtered, i => Assert.Equal(0, i.CaretOffsetDelta));
        Assert.Equal(
            UiText.T("Soql_CompletionRelationFmt", "Account"),
            filtered.Single(i => i.Text == "Account.").Description);

        var functions = SoqlCompletionEngine.FieldItems(describe, "COU", includeFunctions: true);
        Assert.Equal(new[] { "COUNT()", "COUNT(Id)", "COUNT_DISTINCT(Id)" }, functions.Select(i => i.Text));
        Assert.All(functions, i => Assert.Equal(-1, i.CaretOffsetDelta));

        var withoutFunctions = SoqlCompletionEngine.FieldItems(describe, "COU", includeFunctions: false);
        Assert.Empty(withoutFunctions);

        var name = SoqlCompletionEngine.FieldItems(describe, "nam", includeFunctions: true);
        Assert.Equal(new[] { "Name" }, name.Select(i => i.Text));
    }

    [Fact]
    public void Engine_ResolveRelationship_IsCaseInsensitive()
    {
        var describe = ContactDescribe();

        Assert.Equal("Account", SoqlCompletionEngine.ResolveRelationship(describe, "Account"));
        Assert.Equal("Account", SoqlCompletionEngine.ResolveRelationship(describe, "account"));
        Assert.Null(SoqlCompletionEngine.ResolveRelationship(describe, "Unknown"));
    }

    // ---- 件数クエリ ----

    [Fact]
    public void CountBuilder_BuildsFromWhereClause()
    {
        Assert.Equal(
            "SELECT COUNT() FROM Account WHERE Name = 'x'",
            SoqlCountQueryBuilder.Build("SELECT Id FROM Account WHERE Name = 'x'"));

        Assert.Equal(
            "SELECT COUNT() FROM Account",
            SoqlCountQueryBuilder.Build("SELECT Id FROM Account"));
    }

    [Fact]
    public void CountBuilder_StripsOrderLimitOffsetAndSemicolon()
    {
        Assert.Equal(
            "SELECT COUNT() FROM Account WHERE Name != null",
            SoqlCountQueryBuilder.Build("SELECT Id FROM Account WHERE Name != null ORDER BY Name DESC LIMIT 10"));

        Assert.Equal(
            "SELECT COUNT() FROM Account",
            SoqlCountQueryBuilder.Build("SELECT Id FROM Account LIMIT 5;  "));

        Assert.Equal(
            "SELECT COUNT() FROM Account WHERE Name = 'a FROM b'",
            SoqlCountQueryBuilder.Build("SELECT Id FROM Account WHERE Name = 'a FROM b'"));
    }

    [Fact]
    public void CountBuilder_ReturnsNullWhenCountIsNotPossible()
    {
        Assert.Null(SoqlCountQueryBuilder.Build(null));
        Assert.Null(SoqlCountQueryBuilder.Build("   "));
        Assert.Null(SoqlCountQueryBuilder.Build("SELECT Id"));
        Assert.Null(SoqlCountQueryBuilder.Build("SELECT Id FROM Account GROUP BY Name"));
        Assert.Equal(
            "SELECT COUNT() FROM Account WHERE Name = 'GROUP BY'",
            SoqlCountQueryBuilder.Build("SELECT Id FROM Account WHERE Name = 'GROUP BY'"));
    }

    [Fact]
    public void CountBuilder_KeepsCaseOfOriginalQuery()
    {
        Assert.Equal(
            "SELECT COUNT() from Account where Name = 'x'",
            SoqlCountQueryBuilder.Build("select Id from Account where Name = 'x'"));
    }
}
