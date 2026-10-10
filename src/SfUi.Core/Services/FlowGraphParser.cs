using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// フロー Metadata（Tooling API の Metadata フィールド）を読み取り専用グラフへ変換する（Phase 4）。
/// ノード = 要素（start / screens / decisions / record* / actionCalls / loops …）、
/// エッジ = connector / faultConnector / decisions.rules[].connector / loops の各コネクター。
/// 位置は locationX / locationY。全要素に位置が無い場合は BFS の階層レイアウトで補う。
/// </summary>
public static class FlowGraphParser
{
    /// <summary>要素キー（Metadata の型別配列）とカテゴリ トークン。</summary>
    public static readonly (string Key, string Category)[] ElementTypes =
    {
        ("start", "Start"),
        ("screens", "Screen"),
        ("decisions", "Decision"),
        ("assignments", "Assignment"),
        ("recordCreates", "RecordCreate"),
        ("recordUpdates", "RecordUpdate"),
        ("recordLookups", "RecordLookup"),
        ("recordDeletes", "RecordDelete"),
        ("recordRollbacks", "RecordRollback"),
        ("actionCalls", "Action"),
        ("apexPluginCalls", "ApexPlugin"),
        ("subflows", "Subflow"),
        ("loops", "Loop"),
        ("waits", "Wait"),
        ("transforms", "Transform"),
        ("collectionProcessors", "CollectionProcessor"),
        ("customErrors", "CustomError"),
        ("steps", "Step"),
        ("ends", "End"),
        ("orchestratedStages", "Stage"),
        ("dataLookups", "DataLookup"),
    };

    /// <summary>Metadata（JSON オブジェクト）からグラフを組み立てる。</summary>
    public static FlowGraph Parse(JsonElement metadata, string label, string apiName, string versionLabel)
    {
        var elements = new List<(string Category, JsonElement Element, string Name, FlowNodeInfo Node)>();
        var nodes = new List<FlowNodeInfo>();
        var byName = new Dictionary<string, FlowNodeInfo>(StringComparer.Ordinal);

        if (metadata.ValueKind == JsonValueKind.Object)
        {
            var index = 0;
            foreach (var (key, category) in ElementTypes)
            {
                if (!metadata.TryGetProperty(key, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Object)
                {
                    AddElement(category, value);
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in value.EnumerateArray())
                    {
                        AddElement(category, element);
                    }
                }
            }

            void AddElement(string category, JsonElement element)
            {
                var name = GetString(element, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = category == "Start" ? "start" : category + (++index).ToString();
                }

                var id = category + ":" + name;
                var display = GetString(element, "label");
                if (string.IsNullOrWhiteSpace(display))
                {
                    display = category == "Start" ? "Start" : name;
                }

                var node = new FlowNodeInfo(
                    id,
                    name!,
                    display!,
                    category,
                    GetNumber(element, "locationX"),
                    GetNumber(element, "locationY"),
                    BuildDetails(category, element));
                nodes.Add(node);
                elements.Add((category, element, name!, node));
                if (!byName.ContainsKey(name!))
                {
                    byName[name!] = node;
                }
            }
        }

        // ---- エッジ（コネクター）----
        var edges = new List<FlowEdgeInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (category, element, _, node) in elements)
        {
            AddEdge(element, "connector", "normal", null);
            AddEdge(element, "faultConnector", "fault", null);

            if (category == "Decision")
            {
                AddEdge(element, "defaultConnector", "default", GetString(element, "defaultConnectorLabel"));
                if (element.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rule in rules.EnumerateArray())
                    {
                        var ruleLabel = GetString(rule, "label") ?? GetString(rule, "name");
                        AddEdge(rule, "connector", "decision", ruleLabel);
                    }
                }
            }
            else if (category == "Loop")
            {
                AddEdge(element, "nextValueConnector", "loopEach", null);
                AddEdge(element, "noMoreValuesConnector", "loopDone", null);
            }

            void AddEdge(JsonElement owner, string property, string kind, string? label)
            {
                if (!owner.TryGetProperty(property, out var connector) || connector.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                var target = GetString(connector, "targetReference");
                if (string.IsNullOrWhiteSpace(target) || !byName.TryGetValue(target!, out var targetNode))
                {
                    return;
                }

                var key = node.Id + "|" + targetNode.Id + "|" + kind;
                if (seen.Add(key))
                {
                    edges.Add(new FlowEdgeInfo(node.Id, targetNode.Id, kind, label));
                }
            }
        }

        // ---- 位置が全く無い場合は階層レイアウトで補う ----
        var positioned = nodes.Where(n => n.X != 0 || n.Y != 0).ToList();
        if (nodes.Count > 0 && positioned.Count == 0)
        {
            nodes = ApplyLayeredLayout(nodes, edges);
        }

        return new FlowGraph(label, apiName, versionLabel, nodes, edges);
    }

    /// <summary>BFS（最長経路）で階層を決めて座標を割り当てる。</summary>
    internal static List<FlowNodeInfo> ApplyLayeredLayout(IReadOnlyList<FlowNodeInfo> nodes, IReadOnlyList<FlowEdgeInfo> edges)
    {
        const double xGap = 280;
        const double yGap = 120;
        var layer = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            layer[node.Id] = node.Category == "Start" ? 0 : -1;
        }

        // 先行ノードの最大階層 + 1（閉路に備えて最大ノード数回で打ち切る）
        for (var pass = 0; pass < nodes.Count; pass++)
        {
            var changed = false;
            foreach (var edge in edges)
            {
                if (!layer.TryGetValue(edge.FromId, out var from) || from < 0)
                {
                    continue;
                }

                if (!layer.TryGetValue(edge.ToId, out var to))
                {
                    continue;
                }

                if (to < from + 1)
                {
                    layer[edge.ToId] = from + 1;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var fallback = layer.Values.Where(v => v >= 0).DefaultIfEmpty(-1).Max() + 1;
        var rows = new Dictionary<int, int>();
        var result = new List<FlowNodeInfo>();
        foreach (var node in nodes)
        {
            var l = layer[node.Id] >= 0 ? layer[node.Id] : fallback;
            rows.TryGetValue(l, out var row);
            rows[l] = row + 1;
            result.Add(node with { X = 60 + l * xGap, Y = 50 + row * yGap });
        }

        return result;
    }

    private static IReadOnlyList<FlowDetailInfo> BuildDetails(string category, JsonElement element)
    {
        var details = new List<FlowDetailInfo>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                details.Add(new FlowDetailInfo(UiText.T(key), value!));
            }
        }

        Add("SourceEditor_FlowDetail_Object", GetString(element, "object"));
        Add("SourceEditor_FlowDetail_Trigger", GetString(element, "recordTriggerType") ?? GetString(element, "triggerType"));
        Add("SourceEditor_FlowDetail_Flow", GetString(element, "flowName"));
        Add("SourceEditor_FlowDetail_Action", GetString(element, "actionName"));
        Add("SourceEditor_FlowDetail_Collection", GetString(element, "collectionReference"));

        if (category == "Decision" && element.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
        {
            details.Add(new FlowDetailInfo(UiText.T("SourceEditor_FlowDetail_Rules"), UiText.T("SourceEditor_FlowDetail_RulesFmt", rules.GetArrayLength())));
        }

        if (category == "Screen" && element.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
        {
            details.Add(new FlowDetailInfo(UiText.T("SourceEditor_FlowDetail_Fields"), UiText.T("SourceEditor_FlowDetail_FieldsFmt", fields.GetArrayLength())));
        }

        if (category == "Assignment" && element.TryGetProperty("assignmentItems", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            details.Add(new FlowDetailInfo(UiText.T("SourceEditor_FlowDetail_Items"), UiText.T("SourceEditor_FlowDetail_ItemsFmt", items.GetArrayLength())));
        }

        return details;
    }

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double GetNumber(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetDouble(out var number)
            ? number
            : 0;
}
