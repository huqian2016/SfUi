using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>DebugLogParser の単体テスト（サンプルログでの構造・集計・耐性）。</summary>
public class DebugLogParserTests
{
    private const string SampleLog = """
71.0 APEX_CODE,FINEST;APEX_PROFILING,INFO;CALLOUT,INFO;DB,INFO;SYSTEM,DEBUG
07:27:35.0 (1000000)|USER_INFO|[EXTERNAL]|005xx|user@example.com
07:27:35.0 (2000000)|EXECUTION_STARTED
07:27:35.0 (2500000)|CODE_UNIT_STARTED|[EXTERNAL]|01p000|MyClass.run|__sfdc_trigger/AccountTrigger
07:27:35.0 (3000000)|METHOD_ENTRY|[3]|01p000|MyClass.run()
07:27:35.0 (4000000)|SOQL_EXECUTE_BEGIN|[5]|Aggregations:0|SELECT Id, Name FROM Account LIMIT 10
07:27:35.0 (9000000)|SOQL_EXECUTE_END|[5]|Rows:10
07:27:35.0 (10000000)|DML_BEGIN|[7]|Op:Insert|Type:Contact|Rows:2
07:27:35.0 (12000000)|DML_END|[7]
07:27:35.0 (13000000)|EXCEPTION_THROWN|[9]|System.NullPointerException: Attempt to de-reference a null object
07:27:35.0 (14000000)|METHOD_EXIT|[3]|MyClass.run
07:27:35.0 (15000000)|CODE_UNIT_FINISHED|MyClass.run
07:27:35.0 (16000000)|CUMULATIVE_LIMIT_USAGE
07:27:35.0 (16000000)|LIMIT_USAGE_FOR_NS|(default)|
  Number of SOQL queries: 1 out of 100
  Number of query rows: 10 out of 50000
07:27:35.0 (17000000)|EXECUTION_FINISHED
*** Skipped 2048 bytes
""";

    [Fact]
    public void Parse_BuildsEventsAndSummary()
    {
        var analysis = DebugLogParser.Parse(SampleLog);

        Assert.NotNull(analysis.HeaderLine);
        Assert.StartsWith("71.0 APEX_CODE", analysis.HeaderLine);

        var summary = analysis.Summary;
        Assert.Equal(15, summary.EventCount);
        Assert.Equal(15.0, summary.TotalDurationMs, 3);
        Assert.Equal(1, summary.SoqlCount);
        Assert.Equal(10, summary.SoqlRows);
        Assert.Equal(1, summary.DmlCount);
        Assert.Equal(2, summary.DmlRows);
        Assert.Equal(0, summary.CalloutCount);

        var error = Assert.Single(summary.Errors);
        Assert.Equal("System.NullPointerException", error.Type);
        Assert.Equal("Attempt to de-reference a null object", error.Message);
        Assert.Equal(9, error.LineNumber);

        // リミット値は CUMULATIVE ブロックが採用される（使用率降順）
        Assert.Equal(2, summary.Limits.Count);
        Assert.Equal("Number of SOQL queries", summary.Limits[0].Name);
        Assert.Equal(1, summary.Limits[0].Used);
        Assert.Equal(100, summary.Limits[0].Max);
        Assert.Equal("(default)", summary.Limits[0].Namespace);
        Assert.Equal(0.02, summary.Limits[1].Percent!.Value, 4);

        // リミット値の継続行はイベントとして数えない
        Assert.DoesNotContain(analysis.Events, e => e.EventType == "(unknown)" && e.Details.Contains("out of"));
    }

    [Fact]
    public void Parse_BuildsTreeWithDurations()
    {
        var root = DebugLogParser.Parse(SampleLog).Summary.Root;

        var exec = Assert.Single(root.Children);
        Assert.Equal("EXECUTION_STARTED", exec.EventType);
        Assert.Equal(15.0, exec.DurationMs!.Value, 3);

        var codeUnit = Assert.Single(exec.Children);
        Assert.Equal("CODE_UNIT_STARTED", codeUnit.EventType);
        Assert.Equal("MyClass.run", codeUnit.Label);
        Assert.Equal(12.5, codeUnit.DurationMs!.Value, 3);

        var method = Assert.Single(codeUnit.Children);
        Assert.Equal("METHOD_ENTRY", method.EventType);
        Assert.Equal("MyClass.run()", method.Label);
        Assert.Equal(11.0, method.DurationMs!.Value, 3);

        Assert.Equal(2, method.Children.Count);
        var soql = method.Children[0];
        Assert.Equal("SOQL_EXECUTE_BEGIN", soql.EventType);
        Assert.Equal(5.0, soql.DurationMs!.Value, 3);
        Assert.Equal(10, soql.Rows);
        Assert.Contains("SELECT Id, Name FROM Account", soql.Label);

        var dml = method.Children[1];
        Assert.Equal("DML_BEGIN", dml.EventType);
        Assert.Equal(2.0, dml.DurationMs!.Value, 3);
        Assert.Equal(2, dml.Rows);
        Assert.Equal("Op:Insert Type:Contact Rows:2", dml.Label);
    }

    [Fact]
    public void Parse_IncompletePair_LeavesNodeOpen()
    {
        const string log = """
07:27:35.0 (1000000)|EXECUTION_STARTED
07:27:35.0 (2000000)|METHOD_ENTRY|[1]|A|A.m()
07:27:35.0 (3000000)|USER_DEBUG|[2]|DEBUG|hi
""";
        var summary = DebugLogParser.Parse(log).Summary;

        var method = Assert.Single(summary.Root.Children[0].Children);
        Assert.Equal("METHOD_ENTRY", method.EventType);
        Assert.Null(method.DurationMs);
        Assert.Equal(3.0, summary.TotalDurationMs, 3);
    }

    [Fact]
    public void Parse_LimitsWithoutCumulative_UsesLastBlock()
    {
        const string log = """
07:27:35.0 (1000000)|EXECUTION_STARTED
07:27:35.0 (2000000)|LIMIT_USAGE_FOR_NS|(default)|
  Number of SOQL queries: 5 out of 100
07:27:35.0 (3000000)|EXECUTION_FINISHED
""";
        var summary = DebugLogParser.Parse(log).Summary;

        var limit = Assert.Single(summary.Limits);
        Assert.Equal("Number of SOQL queries", limit.Name);
        Assert.Equal(5, limit.Used);
    }

    [Fact]
    public void Parse_UserDebugSummaryAndCategory()
    {
        const string log = "07:27:35.0 (1000000)|USER_DEBUG|[3]|DEBUG|Hello world";
        var analysis = DebugLogParser.Parse(log);

        var ev = Assert.Single(analysis.Events);
        Assert.Equal("Hello world", ev.Summary);
        Assert.Equal(DebugLogEventCategory.Other, ev.Category);
        Assert.Equal(1, ev.LineNumber);
    }

    [Fact]
    public void Parse_NullAndEmpty_ReturnsEmptyAnalysis()
    {
        var analysis = DebugLogParser.Parse(null);
        Assert.Empty(analysis.Events);
        Assert.Equal(0, analysis.Summary.EventCount);
        Assert.Equal(0.0, analysis.Summary.TotalDurationMs, 3);

        analysis = DebugLogParser.Parse("");
        Assert.Empty(analysis.Events);
    }

    [Theory]
    [InlineData("SOQL_EXECUTE_BEGIN", DebugLogEventCategory.Soql)]
    [InlineData("SOSL_EXECUTE_BEGIN", DebugLogEventCategory.Soql)]
    [InlineData("DML_BEGIN", DebugLogEventCategory.Dml)]
    [InlineData("CALLOUT_REQUEST", DebugLogEventCategory.Callout)]
    [InlineData("CODE_UNIT_STARTED", DebugLogEventCategory.Execution)]
    [InlineData("CUMULATIVE_LIMIT_USAGE", DebugLogEventCategory.Execution)]
    [InlineData("METHOD_ENTRY", DebugLogEventCategory.Method)]
    [InlineData("SYSTEM_METHOD_ENTRY", DebugLogEventCategory.Method)]
    [InlineData("CONSTRUCTOR_ENTRY", DebugLogEventCategory.Method)]
    [InlineData("FLOW_ELEMENT_BEGIN", DebugLogEventCategory.Flow)]
    [InlineData("EXCEPTION_THROWN", DebugLogEventCategory.Exception)]
    [InlineData("FATAL_ERROR", DebugLogEventCategory.Exception)]
    [InlineData("VALIDATION_FAIL", DebugLogEventCategory.Exception)]
    [InlineData("VALIDATION_PASS", DebugLogEventCategory.Other)]
    [InlineData("USER_DEBUG", DebugLogEventCategory.Other)]
    [InlineData("HEAP_ALLOCATE", DebugLogEventCategory.Other)]
    public void ClassifyEventType_MapsCategories(string eventType, DebugLogEventCategory expected)
    {
        Assert.Equal(expected, DebugLogParser.ClassifyEventType(eventType));
    }
}
