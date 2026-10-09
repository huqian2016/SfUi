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
        var vars = new Dictionary<string, object?> { ["NOW"] = "固定値", ["YEAR"] = 2026 };
        Assert.Equal("固定値", Eval("[NOW]", vars));
        Assert.Equal(2026, Eval("[YEAR]", vars));
    }

    [Fact]
    public void StringFunctions_Extended()
    {
        Assert.True((bool)Eval("CONTAINS(\"abcdef\", \"cd\")")!);
        Assert.False((bool)Eval("CONTAINS(\"abcdef\", \"xy\")")!);
        Assert.False((bool)Eval("CONTAINS(null, \"cd\")")!);
        Assert.True((bool)Eval("STARTSWITH(\"abcdef\", \"ab\")")!);
        Assert.False((bool)Eval("STARTSWITH(\"abcdef\", \"bc\")")!);
        Assert.True((bool)Eval("ENDSWITH(\"abcdef\", \"ef\")")!);
        Assert.Equal("b", Eval("SPLIT_PART(\"a|b|c\", \"|\", 2)"));
        Assert.Equal("c", Eval("SPLIT_PART(\"a|b|c\", \"|\", 3)"));
        Assert.Null(Eval("SPLIT_PART(\"a|b|c\", \"|\", 4)"));
        Assert.Null(Eval("SPLIT_PART(\"a|b\", \"|\", 0)"));
        Assert.Null(Eval("SPLIT_PART(null, \"|\", 1)"));
        Assert.Equal("007", Eval("LPAD(\"7\", 3, \"0\")"));
        Assert.Equal("x7", Eval("LPAD(\"7\", 2, \"xy\")"));
        Assert.Equal("abc", Eval("LPAD(\"abcdef\", 3, \"0\")"));
        Assert.Equal("700", Eval("RPAD(\"7\", 3, \"0\")"));
        Assert.Equal("abc", Eval("RPAD(\"abcdef\", 3, \"0\")"));
        Assert.Null(Eval("LPAD(null, 3, \"0\")"));
    }

    [Fact]
    public void Numbers_Extended()
    {
        Assert.Equal(12L, Eval("TO_INT(\"12.9\")"));
        Assert.Equal(-12L, Eval("TO_INT(\"-12.9\")"));
        Assert.Null(Eval("TO_INT(\"abc\")"));
        Assert.Null(Eval("TO_INT(null)"));
        Assert.True((bool)Eval("IS_NUMBER(\"1,234.5\")")!);
        Assert.False((bool)Eval("IS_NUMBER(\"abc\")")!);
        Assert.False((bool)Eval("IS_NUMBER(null)")!);
        Assert.Equal("1234.50", Eval("FORMAT_NUMBER(1234.5, \"0.00\")"));
        Assert.Equal("1,234.5", Eval("FORMAT_NUMBER(1234.5, \"#,##0.#\")"));
        Assert.Null(Eval("FORMAT_NUMBER(null, \"0.00\")"));
    }

    [Fact]
    public void Dates_Extended()
    {
        Assert.Equal(new DateTime(2026, 2, 28), Eval("ADD_MONTHS(TO_DATE(\"2026-01-31\"), 1)"));
        Assert.Equal(new DateTime(2025, 12, 31), Eval("ADD_MONTHS(TO_DATE(\"2026-01-31\"), -1)"));
        Assert.Equal(new DateTime(2026, 10, 9, 13, 0, 0), Eval("ADD_HOURS(TO_DATE(\"2026-10-09\"), 13)"));
        Assert.Equal(9, Eval("DIFF_DAYS(TO_DATE(\"2026-01-10\"), TO_DATE(\"2026-01-01\"))"));
        Assert.Equal(-9, Eval("DIFF_DAYS(TO_DATE(\"2026-01-01\"), TO_DATE(\"2026-01-10\"))"));
        Assert.Null(Eval("DIFF_DAYS(null, TO_DATE(\"2026-01-01\"))"));
        Assert.Equal(2026, Eval("YEAR(TO_DATE(\"2026-10-09\"))"));
        Assert.Equal(10, Eval("MONTH(TO_DATE(\"2026-10-09\"))"));
        Assert.Equal(9, Eval("DAY(TO_DATE(\"2026-10-09\"))"));
        Assert.Null(Eval("YEAR(\"not a date\")"));
        var host = new ExpressionHost
        {
            NowProvider = () => new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.FromHours(9)),
        };
        Assert.Equal(new DateTime(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc), Eval("TO_UTC(NOW())", host: host));
    }

    [Fact]
    public void NullIf_And_Iif()
    {
        Assert.Null(Eval("NULLIF(\"a\", \"a\")"));
        Assert.Equal("a", Eval("NULLIF(\"a\", \"b\")"));
        Assert.Equal("", Eval("NULLIF(\"\", null)"));
        Assert.Equal("x", Eval("IIF(LEN(\"abc\") == 3, \"x\", \"y\")"));
        Assert.Equal("x", Eval("IIF(ISBLANK(null), \"x\", \"y\")"));
        Assert.Equal("y", Eval("IIF(IS_NUMBER(\"abc\"), \"x\", \"y\")"));
    }

    [Fact]
    public void SalesforceIdConversion()
    {
        Assert.Equal("003BK00000r7209", Eval("ID15(\"003BK00000r7209YAA\")"));
        Assert.Equal("003BK00000r7209", Eval("ID15(\"003BK00000r7209\")"));
        Assert.Equal("003BK00000r7209YAA", Eval("ID18(\"003BK00000r7209\")"));
        Assert.Equal("003BK00000r7209YAA", Eval("ID18(\"003BK00000r7209YAA\")"));
        Assert.Null(Eval("ID15(\"short\")"));
        Assert.Null(Eval("ID18(null)"));
        Assert.Null(Eval("ID15(\"003BK00000r7209YAAX\")"));
    }

    [Fact]
    public void SyntaxError_Throws()
    {
        var engine = new ExpressionEngine();
        Assert.ThrowsAny<Exception>(() => engine.Compile("LEFT("));
        Assert.ThrowsAny<Exception>(() => engine.Compile("UNKNOWN_FUNC(1)"));
    }
}
