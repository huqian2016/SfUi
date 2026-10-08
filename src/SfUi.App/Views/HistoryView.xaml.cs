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

    /// <summary>複数選択を ViewModel へ反映する（一括削除の対象）。</summary>
    private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid || DataContext is not HistoryViewModel viewModel)
        {
            return;
        }

        var rows = new List<HistoryEntry>();
        foreach (var item in grid.SelectedItems)
        {
            if (item is HistoryEntry entry)
            {
                rows.Add(entry);
            }
        }

        viewModel.SetSelectedEntries(rows);
    }
}
