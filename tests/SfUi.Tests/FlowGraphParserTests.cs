using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>Phase 4: フロー Metadata → 読み取り専用グラフの変換テスト。</summary>
[Collection("Localization")]
public class FlowGraphParserTests
{
    private const string SampleJson = """
        {
          "start": { "locationX": 40, "locationY": 100, "object": "Case", "recordTriggerType": "Create",
                     "connector": { "targetReference": "check_status" } },
          "decisions": [
            { "name": "check_status", "label": "Check Status", "locationX": 300, "locationY": 100,
              "defaultConnector": { "targetReference": "log_error" }, "defaultConnectorLabel": "no",
              "rules": [
                { "label": "Yes", "connector": { "targetReference": "get_account" } },
                { "label": "Maybe", "connector": { "targetReference": "send_screen" } }
              ] }
          ],
          "screens": [
            { "name": "send_screen", "label": "Confirm", "locationX": 560, "locationY": 300,
              "connector": { "targetReference": "get_account" },
              "fields": [ { "name": "f1" }, { "name": "f2" } ] }
          ],
          "recordLookups": [
            { "name": "get_account", "label": "Get Account", "object": "Account",
              "locationX": 800, "locationY": 100,
              "faultConnector": { "targetReference": "log_error" } }
          ],
          "loops": [
            { "name": "loop_items", "collectionReference": "items", "locationX": 300, "locationY": 400,
              "nextValueConnector": { "targetReference": "assign1" },
              "noMoreValuesConnector": { "targetReference": "get_account" } }
          ],
          "assignments": [
            { "name": "assign1", "label": "Set", "assignmentItems": [ { "field": "x" } ],
              "locationX": 560, "locationY": 400 }
          ],
          "customErrors": [
            { "name": "log_error", "label": "Error", "locationX": 800, "locationY": 400 }
          ]
        }
        """;

    private static FlowGraph ParseSample(string json = SampleJson)
    {
        using var document = JsonDocument.Parse(json);
        return FlowGraphParser.Parse(document.RootElement.Clone(), "My Flow", "my_flow", "v3 Active");
    }

    [Fact]
    public void Parse_BuildsNodesForAllElementTypes()
    {
        var graph = ParseSample();

        Assert.Equal(7, graph.Nodes.Count);
        Assert.Equal("My Flow", graph.Label);
        Assert.Equal("my_flow", graph.ApiName);
        Assert.Equal("v3 Active", graph.VersionLabel);

        var start = graph.Nodes.Single(n => n.Category == "Start");
        Assert.Equal("Start", start.Label);
        Assert.Contains(start.Details, d => d.Value == "Case");
        Assert.Contains(start.Details, d => d.Value == "Create");

        var decision = graph.Nodes.Single(n => n.Name == "check_status");
        Assert.Equal("Check Status", decision.Label);
        Assert.Equal(300, decision.X);
        Assert.Equal(100, decision.Y);
        Assert.Contains(decision.Details, d => d.Value == "2"); // Rules 数

        var lookup = graph.Nodes.Single(n => n.Name == "get_account");
        Assert.Contains(lookup.Details, d => d.Value == "Account");

        var screen = graph.Nodes.Single(n => n.Name == "send_screen");
        Assert.Contains(screen.Details, d => d.Value == "2"); // Fields 数

        var loop = graph.Nodes.Single(n => n.Name == "loop_items");
        Assert.Contains(loop.Details, d => d.Value == "items");
    }

    [Fact]
    public void Parse_BuildsEdgesWithKindsAndLabels()
    {
        var graph = ParseSample();

        Assert.Equal(8, graph.Edges.Count);

        string Id(string name) => graph.Nodes.Single(n => n.Name == name).Id;

        Assert.Contains(graph.Edges, e => e.FromId == Id("start") && e.ToId == Id("check_status") && e.Kind == "normal");
        Assert.Contains(graph.Edges, e => e.FromId == Id("check_status") && e.ToId == Id("get_account") && e.Kind == "decision" && e.Label == "Yes");
        Assert.Contains(graph.Edges, e => e.FromId == Id("check_status") && e.ToId == Id("send_screen") && e.Kind == "decision" && e.Label == "Maybe");
        Assert.Contains(graph.Edges, e => e.FromId == Id("check_status") && e.ToId == Id("log_error") && e.Kind == "default" && e.Label == "no");
        Assert.Contains(graph.Edges, e => e.FromId == Id("send_screen") && e.ToId == Id("get_account") && e.Kind == "normal");
        Assert.Contains(graph.Edges, e => e.FromId == Id("get_account") && e.ToId == Id("log_error") && e.Kind == "fault");
        Assert.Contains(graph.Edges, e => e.FromId == Id("loop_items") && e.ToId == Id("assign1") && e.Kind == "loopEach");
        Assert.Contains(graph.Edges, e => e.FromId == Id("loop_items") && e.ToId == Id("get_account") && e.Kind == "loopDone");
    }

    [Fact]
    public void Parse_MissingTargets_AreIgnored()
    {
        var graph = ParseSample("""
            { "start": { "connector": { "targetReference": "does_not_exist" } },
              "screens": [ { "name": "s1", "locationX": 10, "locationY": 10 } ] }
            """);

        Assert.Equal(2, graph.Nodes.Count);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void Parse_EmptyMetadata_ReturnsEmptyGraph()
    {
        var graph = ParseSample("{}");
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void Parse_WithoutLocations_AppliesLayeredLayout()
    {
        var graph = ParseSample("""
            { "start": { "connector": { "targetReference": "a" } },
              "recordLookups": [ { "name": "a", "connector": { "targetReference": "b" } } ],
              "screens": [ { "name": "b" } ] }
            """);

        var start = graph.Nodes.Single(n => n.Name == "start");
        var a = graph.Nodes.Single(n => n.Name == "a");
        var b = graph.Nodes.Single(n => n.Name == "b");

        Assert.Equal(60, start.X);
        Assert.Equal(60 + 280, a.X);
        Assert.Equal(60 + 560, b.X);
        Assert.True(start.Y < 200 && a.Y < 200 && b.Y < 200);
    }

    [Fact]
    public void Parse_DuplicateConnector_IsDeduplicated()
    {
        // connector と loopEach が同じターゲットを指しても 1 本にまとまる（同一 kind の重複のみ排除）
        var graph = ParseSample("""
            { "start": { "locationX": 10, "locationY": 10, "connector": { "targetReference": "x" } },
              "screens": [ { "name": "x", "locationX": 300, "locationY": 10 } ] }
            """);
        Assert.Single(graph.Edges);
    }
}
