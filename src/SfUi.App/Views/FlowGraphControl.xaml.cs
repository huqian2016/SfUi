using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>フロー グラフ表示コントロール（Phase 4。読み取り専用 + ズーム + ノード詳細）。</summary>
public partial class FlowGraphControl : UserControl
{
    public FlowGraphControl()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FlowGraphViewModel graph)
        {
            graph.Close();
        }
    }

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is FlowNodeViewModel node && DataContext is FlowGraphViewModel graph)
        {
            graph.SelectedNode = node;
        }
    }
}
