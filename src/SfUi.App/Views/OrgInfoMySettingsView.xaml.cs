using System.Windows.Controls;
using System.Windows.Input;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>マイ設定タブ（カスタムタブの管理と項目ピッカー）。</summary>
public partial class OrgInfoMySettingsView : UserControl
{
    public OrgInfoMySettingsView()
    {
        InitializeComponent();
    }

    private void AvailableList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OrgInfoMySettingsViewModel viewModel && AvailableList.SelectedItem is AvailableItemView item)
        {
            viewModel.AddItemCommand.Execute(item);
        }
    }

    private void SelectedList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OrgInfoMySettingsViewModel viewModel && SelectedList.SelectedItem is SelectedItemView item)
        {
            viewModel.RemoveItemCommand.Execute(item);
        }
    }
}
