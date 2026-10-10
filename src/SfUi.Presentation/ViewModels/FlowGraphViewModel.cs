using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>フロー グラフの 1 ノード（表示用。カテゴリ色と表示ラベル付き）。</summary>
public sealed class FlowNodeViewModel
{
    public const double NodeWidth = 210;
    public const double NodeHeight = 76;

    public FlowNodeViewModel(FlowNodeInfo node)
    {
        Id = node.Id;
        Name = node.Name;
        Label = node.Label;
        Category = node.Category;
        CategoryLabel = CategoryLabelFor(node.Category);
        X = node.X;
        Y = node.Y;
        Width = NodeWidth;
        Height = NodeHeight;
        Details = node.Details;
        (BackgroundHex, BorderHex) = ColorsFor(node.Category);
    }

    public string Id { get; }

    public string Name { get; }

    public string Label { get; }

    public string Category { get; }

    public string CategoryLabel { get; }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public IReadOnlyList<FlowDetailInfo> Details { get; }

    public string BackgroundHex { get; }

    public string BorderHex { get; }

    public override string ToString() => Label;

    /// <summary>カテゴリの表示名（現在の UI 言語）。</summary>
    public static string CategoryLabelFor(string category) => category switch
    {
        "Start" => UiText.T("SourceEditor_FlowType_Start"),
        "Screen" => UiText.T("SourceEditor_FlowType_Screen"),
        "Decision" => UiText.T("SourceEditor_FlowType_Decision"),
        "Assignment" => UiText.T("SourceEditor_FlowType_Assignment"),
        "RecordCreate" => UiText.T("SourceEditor_FlowType_RecordCreate"),
        "RecordUpdate" => UiText.T("SourceEditor_FlowType_RecordUpdate"),
        "RecordLookup" => UiText.T("SourceEditor_FlowType_RecordLookup"),
        "RecordDelete" => UiText.T("SourceEditor_FlowType_RecordDelete"),
        "RecordRollback" => UiText.T("SourceEditor_FlowType_RecordRollback"),
        "Action" => UiText.T("SourceEditor_FlowType_Action"),
        "ApexPlugin" => UiText.T("SourceEditor_FlowType_ApexPlugin"),
        "Subflow" => UiText.T("SourceEditor_FlowType_Subflow"),
        "Loop" => UiText.T("SourceEditor_FlowType_Loop"),
        "Wait" => UiText.T("SourceEditor_FlowType_Wait"),
        "Transform" => UiText.T("SourceEditor_FlowType_Transform"),
        "CollectionProcessor" => UiText.T("SourceEditor_FlowType_CollectionProcessor"),
        "CustomError" => UiText.T("SourceEditor_FlowType_CustomError"),
        "Step" => UiText.T("SourceEditor_FlowType_Step"),
        "End" => UiText.T("SourceEditor_FlowType_End"),
        "Stage" => UiText.T("SourceEditor_FlowType_Stage"),
        "DataLookup" => UiText.T("SourceEditor_FlowType_DataLookup"),
        _ => UiText.T("SourceEditor_FlowType_Other"),
    };

    /// <summary>カテゴリごとの背景色 / 枠線色（淡色 + 濃色のペア）。</summary>
    public static (string Background, string Border) ColorsFor(string category) => category switch
    {
        "Start" => ("#E8F5E9", "#2E7D32"),
        "End" => ("#ECEFF1", "#546E7A"),
        "Screen" => ("#E3F2FD", "#1565C0"),
        "Decision" => ("#FFF8E1", "#F9A825"),
        "Assignment" => ("#F1F8E9", "#558B2F"),
        "RecordCreate" or "RecordUpdate" or "RecordLookup" or "RecordDelete" or "RecordRollback" => ("#E0F2F1", "#00695C"),
        "Action" => ("#F3E5F5", "#6A1B9A"),
        "ApexPlugin" => ("#EDE7F6", "#4527A0"),
        "Subflow" => ("#E8EAF6", "#283593"),
        "Loop" => ("#FFF3E0", "#E65100"),
        "Wait" => ("#ECEFF1", "#455A64"),
        "Transform" or "CollectionProcessor" => ("#E0F7FA", "#00838F"),
        "CustomError" => ("#FFEBEE", "#C62828"),
        "Step" => ("#F5F5F5", "#616161"),
        "Stage" => ("#EFEBE9", "#5D4037"),
        "DataLookup" => ("#E1F5FE", "#0277BD"),
        _ => ("#F5F5F5", "#9E9E9E"),
    };
}

/// <summary>フロー グラフの 1 接続（ノード境界で切った線分とラベル）。</summary>
public sealed class FlowEdgeViewModel
{
    public FlowEdgeViewModel(FlowEdgeInfo edge, FlowNodeViewModel from, FlowNodeViewModel to)
    {
        (X1, Y1) = EdgePoint(from, to);
        (X2, Y2) = EdgePoint(to, from);
        LabelX = (X1 + X2) / 2;
        LabelY = (Y1 + Y2) / 2 - 7;

        StrokeHex = edge.Kind switch
        {
            "fault" => "#C62828",
            "decision" => "#1565C0",
            "default" => "#6A1B9A",
            "loopEach" => "#E65100",
            "loopDone" => "#2E7D32",
            _ => "#9E9E9E",
        };

        var label = edge.Label ?? edge.Kind switch
        {
            "fault" => UiText.T("SourceEditor_FlowEdge_Fault"),
            "loopEach" => UiText.T("SourceEditor_FlowEdge_Each"),
            "loopDone" => UiText.T("SourceEditor_FlowEdge_Done"),
            "default" => UiText.T("SourceEditor_FlowEdge_Default"),
            _ => null,
        };
        Label = label ?? string.Empty;
        HasLabel = !string.IsNullOrEmpty(Label);
    }

    public double X1 { get; }

    public double Y1 { get; }

    public double X2 { get; }

    public double Y2 { get; }

    public double LabelX { get; }

    public double LabelY { get; }

    public string StrokeHex { get; }

    public string Label { get; }

    public bool HasLabel { get; }

    /// <summary>ノード中心から相手方向へ伸ばした線が、ノード矩形の境界と交わる点。</summary>
    internal static (double X, double Y) EdgePoint(FlowNodeViewModel node, FlowNodeViewModel other)
    {
        var cx = node.X + FlowNodeViewModel.NodeWidth / 2;
        var cy = node.Y + FlowNodeViewModel.NodeHeight / 2;
        var ox = other.X + FlowNodeViewModel.NodeWidth / 2;
        var oy = other.Y + FlowNodeViewModel.NodeHeight / 2;
        var dx = ox - cx;
        var dy = oy - cy;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            return (cx, cy);
        }

        var sx = dx != 0 ? FlowNodeViewModel.NodeWidth / 2 / Math.Abs(dx) : double.PositiveInfinity;
        var sy = dy != 0 ? FlowNodeViewModel.NodeHeight / 2 / Math.Abs(dy) : double.PositiveInfinity;
        var s = Math.Min(Math.Min(sx, sy), 1.0);
        return (cx + dx * s, cy + dy * s);
    }
}

/// <summary>フロー 1 件のグラフ表示 VM（Phase 4。読み取り専用）。</summary>
public sealed partial class FlowGraphViewModel : ObservableObject
{
    public FlowGraphViewModel(FlowGraph graph)
    {
        Label = graph.Label;
        ApiName = graph.ApiName;
        VersionLabel = graph.VersionLabel;
        HeaderText = graph.Label + " (" + graph.VersionLabel + ")";

        var byId = new Dictionary<string, FlowNodeViewModel>(StringComparer.Ordinal);
        var nodes = new List<FlowNodeViewModel>();
        foreach (var node in graph.Nodes)
        {
            var nodeViewModel = new FlowNodeViewModel(node);
            nodes.Add(nodeViewModel);
            byId[node.Id] = nodeViewModel;
        }

        var edges = new List<FlowEdgeViewModel>();
        foreach (var edge in graph.Edges)
        {
            if (byId.TryGetValue(edge.FromId, out var from) && byId.TryGetValue(edge.ToId, out var to))
            {
                edges.Add(new FlowEdgeViewModel(edge, from, to));
            }
        }

        Nodes = new ObservableCollection<FlowNodeViewModel>(nodes);
        Edges = new ObservableCollection<FlowEdgeViewModel>(edges);
        CountText = UiText.T("SourceEditor_FlowCountsFmt", graph.Nodes.Count, graph.Edges.Count);
        CanvasWidth = Math.Max(640, nodes.Select(n => n.X + FlowNodeViewModel.NodeWidth).DefaultIfEmpty(0).Max() + 120);
        CanvasHeight = Math.Max(420, nodes.Select(n => n.Y + FlowNodeViewModel.NodeHeight).DefaultIfEmpty(0).Max() + 120);
    }

    /// <summary>閉じる要求（エディタ VM が処理してグラフ表示を解除する）。</summary>
    public event Action? CloseRequested;

    public string Label { get; }

    public string ApiName { get; }

    public string VersionLabel { get; }

    public string HeaderText { get; }

    public string CountText { get; }

    public ObservableCollection<FlowNodeViewModel> Nodes { get; }

    public ObservableCollection<FlowEdgeViewModel> Edges { get; }

    public double CanvasWidth { get; }

    public double CanvasHeight { get; }

    [ObservableProperty]
    private double _zoom = 1.0;

    [ObservableProperty]
    private FlowNodeViewModel? _selectedNode;

    public bool HasSelection => SelectedNode is not null;

    public IReadOnlyList<FlowDetailInfo> SelectedNodeDetails => SelectedNode?.Details ?? Array.Empty<FlowDetailInfo>();

    public string SelectedNodeTitle => SelectedNode is null
        ? UiText.T("SourceEditor_FlowNoSelection")
        : SelectedNode.Label + " (" + SelectedNode.CategoryLabel + ")";

    partial void OnSelectedNodeChanged(FlowNodeViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedNodeDetails));
        OnPropertyChanged(nameof(SelectedNodeTitle));
    }

    public void Close() => CloseRequested?.Invoke();
}
