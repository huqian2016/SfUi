using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>フロー グラフ表示コントロール（Phase 4。読み取り専用 + ズーム + ノード詳細）。</summary>
public partial class FlowGraphControl : UserControl
{
    public FlowGraphControl()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RenderGraph();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FlowGraphViewModel graph)
        {
            graph.Close();
        }
    }

    private void Node_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is FlowNodeViewModel node && DataContext is FlowGraphViewModel graph)
        {
            graph.SelectedNode = node;
        }
    }

    /// <summary>グラフ（エッジ + ノード）を Canvas へ描画する（Avalonia は複数プロパティのバインディングやコンテナーの位置スタイルが使えないため）。</summary>
    private void RenderGraph()
    {
        GraphCanvas.Children.Clear();
        if (DataContext is not FlowGraphViewModel graph)
        {
            return;
        }

        // エッジ（ノードの下に描画）
        foreach (var edge in graph.Edges)
        {
            GraphCanvas.Children.Add(new Line
            {
                StartPoint = new Point(edge.X1, edge.Y1),
                EndPoint = new Point(edge.X2, edge.Y2),
                Stroke = BrushFor(edge.StrokeHex),
                StrokeThickness = 1.6,
            });

            if (edge.HasLabel)
            {
                var label = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(3, 1),
                    Child = new TextBlock { Text = edge.Label, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)) },
                };
                Canvas.SetLeft(label, edge.LabelX);
                Canvas.SetTop(label, edge.LabelY);
                GraphCanvas.Children.Add(label);
            }
        }

        // ノード
        foreach (var node in graph.Nodes)
        {
            var border = new Border
            {
                Width = node.Width,
                Height = node.Height,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                Background = BrushFor(node.BackgroundHex),
                BorderBrush = BrushFor(node.BorderHex),
                Cursor = new Cursor(StandardCursorType.Hand),
                DataContext = node,
                Child = new StackPanel
                {
                    Margin = new Thickness(8, 6),
                    Children =
                    {
                        new TextBlock { Text = node.CategoryLabel, FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = BrushFor(node.BorderHex) },
                        new TextBlock { Text = node.Label, TextWrapping = TextWrapping.Wrap, MaxHeight = 36, Margin = new Thickness(0, 2, 0, 0) },
                    },
                },
            };
            AutomationProperties.SetName(border, node.Label);
            border.PointerPressed += Node_PointerPressed;
            Canvas.SetLeft(border, node.X);
            Canvas.SetTop(border, node.Y);
            GraphCanvas.Children.Add(border);
        }
    }

    private static IBrush BrushFor(string hex)
        => Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;
}
