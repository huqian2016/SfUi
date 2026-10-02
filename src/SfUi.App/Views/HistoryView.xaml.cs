using System.Windows.Controls;
using System.Windows.Input;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    private void Row_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: HistoryEntry entry } && DataContext is HistoryViewModel viewModel)
        {
            viewModel.RequestReplay(entry);
            e.Handled = true;
        }
    }
}
