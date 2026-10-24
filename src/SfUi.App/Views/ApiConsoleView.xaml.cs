using System.Windows;
using System.Windows.Controls;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

public partial class ApiConsoleView : UserControl
{
    public ApiConsoleView() => InitializeComponent();

    /// <summary>TreeView.SelectedItem は読み取り専用なので、選択を VM へ渡す。</summary>
    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ApiConsoleViewModel vm && e.NewValue is RestTreeNodeViewModel node)
        {
            vm.SelectedTreeNode = node;
        }
    }
}
