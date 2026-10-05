namespace SfUi.Core;

/// <summary>
/// SOQL エディターの構文ハイライト（XSHD）定義。
/// WPF（AvalonEdit）と Avalonia（AvaloniaEdit）の両方で HighlightingLoader から読み込める。
/// </summary>
public static class SoqlHighlighting
{
    /// <summary>キーワード色（青）。</summary>
    public const string KeywordColor = "#0000CC";

    /// <summary>関数・日付リテラルの色。</summary>
    public const string FunctionColor = "#795E26";

    /// <summary>青で強調する予約語。</summary>
    public static readonly IReadOnlyList<string> Keywords = new[]
    {
        "SELECT", "FROM", "WHERE", "GROUP", "BY", "ORDER", "LIMIT", "OFFSET",
        "WITH", "DATA", "CATEGORY", "ABOVE", "BELOW", "AT",
        "TYPEOF", "WHEN", "THEN", "ELSE", "END",
        "AND", "OR", "NOT", "IN", "LIKE", "INCLUDES", "EXCLUDES",
        "TRUE", "FALSE", "NULL", "NULLS", "FIRST", "LAST",
        "ASC", "DESC", "DISTINCT",
        "FOR", "VIEW", "REFERENCE", "UPDATE", "TRACKING",
        "SECURITY_ENFORCED", "USER_MODE", "SYSTEM_MODE", "SNIPPET", "METADATA",
        "USING", "SCOPE", "AS", "ALL",
    };

    /// <summary>関数・日付リテラル（別色）として強調する語。</summary>
    public static readonly IReadOnlyList<string> Functions = new[]
    {
        "COUNT", "COUNT_DISTINCT", "SUM", "AVG", "MIN", "MAX",
        "CALENDAR_MONTH", "CALENDAR_QUARTER", "CALENDAR_YEAR",
        "FISCAL_MONTH", "FISCAL_QUARTER", "FISCAL_YEAR", "FISCAL_WEEK",
        "DAY_IN_MONTH", "DAY_IN_WEEK", "DAY_IN_YEAR", "DAY_ONLY",
        "HOUR_IN_DAY", "WEEK_IN_MONTH", "WEEK_IN_YEAR",
        "FORMAT", "CONVERTCURRENCY", "CONVERTTIMEZONE", "TO_LABEL", "GROUPING",
        "YESTERDAY", "TODAY", "TOMORROW",
        "LAST_WEEK", "THIS_WEEK", "NEXT_WEEK",
        "LAST_MONTH", "THIS_MONTH", "NEXT_MONTH",
        "LAST_90_DAYS", "NEXT_90_DAYS", "LAST_N_DAYS", "NEXT_N_DAYS",
        "THIS_QUARTER", "LAST_QUARTER", "NEXT_QUARTER",
        "THIS_YEAR", "LAST_YEAR", "NEXT_YEAR",
        "THIS_FISCAL_QUARTER", "LAST_FISCAL_QUARTER", "NEXT_FISCAL_QUARTER",
        "THIS_FISCAL_YEAR", "LAST_FISCAL_YEAR", "NEXT_FISCAL_YEAR",
    };

    /// <summary>エディターへ読み込む XSHD（XML）文字列。</summary>
    public static string Xshd { get; } = BuildXshd();

    private static string BuildXshd()
    {
        var keywords = string.Concat(Keywords.Select(word => $"      <Word>{word}</Word>\n"));
        var functions = string.Concat(Functions.Select(word => $"      <Word>{word}</Word>\n"));
        return $"""
<?xml version="1.0"?>
<SyntaxDefinition name="SOQL" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Comment" foreground="#008000" />
  <Color name="String" foreground="#A31515" />
  <Color name="Keyword" foreground="{KeywordColor}" />
  <Color name="Function" foreground="{FunctionColor}" />
  <Color name="Number" foreground="#098658" />
  <RuleSet>
    <Span color="Comment" begin="--" />
    <Span color="String" multiline="false" begin="'" end="'" />
    <Keywords color="Keyword">
{keywords}    </Keywords>
    <Keywords color="Function">
{functions}    </Keywords>
    <Rule color="Number">\b\d+(\.\d+)?\b</Rule>
  </RuleSet>
</SyntaxDefinition>
""";
    }
}
