namespace SfUi.Core;

/// <summary>フロー グラフの 1 ノード（フロー要素）。</summary>
/// <param name="Id">一意キー（例: Decision:check_status）。</param>
/// <param name="Name">要素の API 名（Metadata の name。start は "start"）。</param>
/// <param name="Label">表示ラベル（label が無ければ name）。</param>
/// <param name="Category">要素カテゴリ（Start / Screen / Decision / RecordLookup …）。</param>
/// <param name="X">キャンバス X 座標（locationX。無い場合はレイアウト計算値）。</param>
/// <param name="Y">キャンバス Y 座標（locationY）。</param>
/// <param name="Details">ノード詳細（ラベル + 値）。</param>
public sealed record FlowNodeInfo(
    string Id,
    string Name,
    string Label,
    string Category,
    double X,
    double Y,
    IReadOnlyList<FlowDetailInfo> Details);

/// <summary>ノード詳細の 1 行。</summary>
public sealed record FlowDetailInfo(string Label, string Value);

/// <summary>フロー グラフの 1 接続。</summary>
/// <param name="Kind">normal / fault / decision / default / loopEach / loopDone。</param>
/// <param name="Label">表示ラベル（decision の rule ラベルなど。null = 種別の既定表示）。</param>
public sealed record FlowEdgeInfo(string FromId, string ToId, string Kind, string? Label);

/// <summary>フロー 1 件の読み取り専用グラフ。</summary>
public sealed record FlowGraph(
    string Label,
    string ApiName,
    string VersionLabel,
    IReadOnlyList<FlowNodeInfo> Nodes,
    IReadOnlyList<FlowEdgeInfo> Edges);
