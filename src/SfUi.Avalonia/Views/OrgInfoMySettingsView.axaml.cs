using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>マイ設定タブ（カスタムタブの管理と項目ピッカー）。</summary>
public partial class OrgInfoMySettingsView : UserControl
{
    public OrgInfoMySettingsView() => AvaloniaXamlLoader.Load(this);

    private void AvailableList_MouseDoubleClick(object? sender, TappedEventArgs e)
    {
        if (DataContext is OrgInfoMySettingsViewModel viewModel
            && this.FindControl<ListBox>("AvailableList")?.SelectedItem is AvailableItemView item
            && !item.IsHeader)
        {
            viewModel.AddItemCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void SelectedList_MouseDoubleClick(object? sender, TappedEventArgs e)
    {
        if (DataContext is OrgInfoMySettingsViewModel viewModel
            && this.FindControl<ListBox>("SelectedList")?.SelectedItem is SelectedItemView item)
        {
            viewModel.RemoveItemCommand.Execute(item);
            e.Handled = true;
        }
    }
}
