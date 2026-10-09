using SfUi.Etl.Expressions;
using Xunit;

namespace SfUi.Tests;

/// <summary>式言語（DynamicExpresso ラッパー + 関数ライブラリ）を検証する。</summary>
public class ExpressionEngineTests
{
    private static object? Eval(
        string expression,
        Dictionary<string, object?>? variables = null,
        ExpressionHost? host = null)
    {
        var vars = variables ?? new Dictionary<string, object?>();
        var compiled = new ExpressionEngine(host).Compile(expression, vars.Keys.ToArray());
        return compiled.Evaluate(vars);
    }

    [Fact]
    public void ConstantAndText()
    {
        Assert.Equal("固定", Eval("\"固定\""));
        Assert.Equal("123", Eval("TEXT(123)"));
        Assert.Equal("1.5", Eval("TEXT(1.5)"));
        Assert.Null(Eval("TEXT(null)"));
    }

    [Fact]
    public void StringFunctions()
    {
        Assert.Equal("東京都", Eval("LEFT(\"東京都港区\", 3)"));
        Assert.Equal("港区", Eval("RIGHT(\"東京都港区\", 2)"));
        Assert.Equal("京都", Eval("MID(\"東京都港区\", 2, 2)"));
        Assert.Equal(3, Eval("LEN(\"あいう\")"));
        Assert.Equal("abc", Eval("TRIM(\"  abc  \")"));
        Assert.Equal("ABC", Eval("UPPER(\"abc\")"));
        Assert.Equal("abc", Eval("LOWER(\"ABC\")"));
        Assert.Equal("a-b", Eval("REPLACE(\"a b\", \" \", \"-\")"));
        Assert.Equal("山田 太郎", Eval("CONCAT(\"山田\", \" \", \"太郎\")"));
        Assert.Equal("ab", Eval("CONCAT(\"a\", null, \"b\")"));
        Assert.Equal(2, ((string[])Eval("SPLIT(\"a|b\", \"|\")")!).Length);
        Assert.Equal("a-b", Eval("JOIN(\"-\", SPLIT(\"a|b\", \"|\"))"));
    }

    [Fact]
    public void NullPropagation()
    {
        Assert.Null(Eval("LEFT(null, 2)"));
        Assert.Null(Eval("TRIM(null)"));
        Assert.Equal(0, Eval("LEN(null)"));
        Assert.Equal("x", Eval("IFNULL(null, \"x\")"));
        Assert.Equal("v", Eval("IFNULL(\"v\", \"x\")"));
        Assert.Equal("b", Eval("COALESCE(null, \"b\")"));
    }

    [Fact]
    public void ColumnReferences_BareAndBracket()
    {
        var vars = new Dictionary<string, object?>
        {
            ["Name"] = "ACME",
            ["Account.Id"] = "001AAA",
            ["取引先名"] = "テスト商事",
            ["My Col"] = "v1",
        };

        Assert.Equal("ACME", Eval("Name", vars));
        Assert.Equal("001AAA", Eval("[Account.Id]", vars));
        Assert.Equal("テスト商事", Eval("[取引先名]", vars));
        Assert.Equal("v1", Eval("[My Col]", vars));
        Assert.Equal("ACME (001AAA)", Eval("CONCAT(Name, \" (\", [Account.Id], \")\")", vars));
    }

    [Fact]
    public void ReusableCompiledExpression_Positional()
    {
        var compiled = new ExpressionEngine().Compile("CONCAT([姓], \" \", [名])", new[] { "姓", "名" });

        Assert.Equal(new[] { "姓", "名" }, compiled.Variables);
        Assert.Equal("山田 太郎", compiled.EvaluateArgs("山田", "太郎"));
        Assert.Equal("鈴木 花子", compiled.EvaluateArgs("鈴木", "花子"));
    }

    [Fact]
    public void Numbers()
    {
        Assert.Equal(1234.5m, Eval("TO_NUMBER(\"1,234.5\")"));
        Assert.Null(Eval("TO_NUMBER(\"abc\")"));
        Assert.Equal(1.23m, Eval("ROUND(TO_NUMBER(\"1.2345\"), 2)"));
        Assert.Equal(5m, Eval("ABS(TO_NUMBER(\"-5\"))"));
        Assert.Equal(2m, Eval("MIN(TO_NUMBER(\"2\"), TO_NUMBER(\"5\"))"));
        Assert.Equal(2m, Eval("FLOOR(TO_NUMBER(\"2.9\"))"));
        Assert.Equal(3m, Eval("CEIL(TO_NUMBER(\"2.1\"))"));
    }

    [Fact]
    public void Dates()
    {
        Assert.Equal(new DateTime(2026, 10, 9), Eval("TO_DATE(\"2026/10/09\")"));
        Assert.Equal(new DateTime(2026, 10, 9), Eval("TO_DATE(\"2026-10-09\")"));
        Assert.Equal("2026/10/09", Eval("FORMAT_DATE(TO_DATE(\"2026-10-09\"), \"yyyy/MM/dd\")"));
        Assert.Equal(new DateTime(2026, 10, 16), Eval("ADD_DAYS(TO_DATE(\"2026-10-09\"), 7)"));
        Assert.Equal("2026-10-09T00:00:00", Eval("TEXT(TO_DATE(\"2026-10-09\"))"));
        Assert.Null(Eval("TO_DATE(\"not a date\")"));
    }

    [Fact]
    public void SystemValues()
    {
        var host = new ExpressionHost
        {
            UserName = "user1",
            OrgName = "hks4sand1",
            MachineName = "PC1",
            NowProvider = () => new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.FromHours(9)),
            TodayProvider = () => new DateTime(2026, 10, 9),
            RowNumberProvider = () => 5,
            GuidProvider = () => "11111111-2222-3333-4444-555555555555",
        };

        Assert.Equal("user1", Eval("CURRENT_USER()", host: host));
        Assert.Equal("hks4sand1", Eval("CURRENT_ORG()", host: host));
        Assert.Equal("PC1", Eval("CURRENT_PC()", host: host));
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.FromHours(9)), Eval("NOW()", host: host));
        Assert.Equal(new DateTime(2026, 10, 9), Eval("TODAY()", host: host));
        Assert.Equal(5, Eval("ROW_NUMBER()", host: host));
        Assert.Equal("11111111-2222-3333-4444-555555555555", Eval("GUID()", host: host));
    }

    [Fact]
    public void LookupPrevAndParent()
    {
        var host = new ExpressionHost
        {
            Lookup = (obj, key, value) => obj + ":" + key + ":" + value,
            Prev = field => field == "Amt" ? (object?)"100" : null,
            Parent = field => "P-" + field,
        };
        var vars = new Dictionary<string, object?> { ["ExtId"] = "E1" };

        Assert.Equal("Account:ExternalId:E1", Eval("LOOKUP(\"Account\", \"ExternalId\", [ExtId])", vars, host));
        Assert.Equal("100", Eval("PREV(\"Amt\")", host: host));
        Assert.Null(Eval("PREV(\"Other\")", host: host));
        Assert.Equal("P-AccountId", Eval("PARENT(\"AccountId\")", host: host));
    }

    [Fact]
    public void BlankAndConditionals()
    {
        Assert.True((bool)Eval("ISBLANK(null)")!);
        Assert.True((bool)Eval("ISBLANK(\"\")")!);
        Assert.True((bool)Eval("ISBLANK(\"  \")")!);
        Assert.False((bool)Eval("ISBLANK(\"x\")")!);

        var vars = new Dictionary<string, object?> { ["Memo"] = null, ["Name"] = "ACME" };
        Assert.Equal("無", Eval("IF(ISBLANK([Memo]), \"無\", [Memo])", vars));
        Assert.Equal("ACME", Eval("IF(ISBLANK([Name]), \"無\", [Name])", vars));
    }

    [Fact]
    public void ReservedNameColumn_RequiresBrackets()
    {
        var vars = new Dictionary<string, object?> { ["NOW"] = "固定値" };
        Assert.Equal("固定値", Eval("[NOW]", vars));
    }

    [Fact]
    public void SyntaxError_Throws()
    {
        var engine = new ExpressionEngine();
        Assert.ThrowsAny<Exception>(() => engine.Compile("LEFT("));
        Assert.ThrowsAny<Exception>(() => engine.Compile("UNKNOWN_FUNC(1)"));
    }
}
