using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

public partial class QuickPanelView : UserControl
{
    public QuickPanelView() => AvaloniaXamlLoader.Load(this);

    private QuickPanelViewModel? ViewModel => DataContext as QuickPanelViewModel;

    private QuickPanelItem? SelectedItem =>
        this.FindControl<ListBox>("FavoritesList")?.SelectedItem as QuickPanelItem;

    private void OnItemDoubleClick(object? sender, TappedEventArgs e)
    {
        if (SelectedItem is { } item && ViewModel is { } vm)
        {
            vm.RequestExecute(item.Favorite);
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedItem is { } item && ViewModel is { } vm)
        {
            vm.RequestExecute(item.Favorite);
            e.Handled = true;
        }
    }

    private void OnExecuteClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem is { } item && ViewModel is { } vm)
        {
            vm.RequestExecute(item.Favorite);
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem is { } item && ViewModel is { } vm)
        {
            vm.Remove(item.Favorite);
        }
    }
}
