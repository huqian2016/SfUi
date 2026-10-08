using System.Windows.Controls;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>復元タブ。</summary>
public partial class RestoreTabView : UserControl
{
    public RestoreTabView() => InitializeComponent();

    /// <summary>複数選択を ViewModel へ反映する（一括削除の対象）。</summary>
    private void BackupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list || DataContext is not RestoreTabViewModel viewModel)
        {
            return;
        }

        var rows = new List<BackupListItemViewModel>();
        foreach (var item in list.SelectedItems)
        {
            if (item is BackupListItemViewModel row)
            {
                rows.Add(row);
            }
        }

        viewModel.SetSelectedBackups(rows);
    }
}
