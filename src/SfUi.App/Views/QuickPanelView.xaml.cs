using System.Windows.Controls;
using System.Windows.Input;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

public partial class QuickPanelView : UserControl
{
    public QuickPanelView()
    {
        InitializeComponent();
    }

    private QuickPanelViewModel? ViewModel => DataContext as QuickPanelViewModel;

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FavoritesList.SelectedItem is QuickPanelItem item && ViewModel is { } vm)
        {
            vm.RequestExecute(item.Favorite);
            e.Handled = true;
        }
    }

    private void OnListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && FavoritesList.SelectedItem is QuickPanelItem item && ViewModel is { } vm)
        {
            vm.RequestExecute(item.Favorite);
            e.Handled = true;
        }
    }

    private void OnExecuteClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (FavoritesList.SelectedItem is QuickPanelItem item && ViewModel is { } vm)
        {
            vm.RequestExecute(item.Favorite);
        }
    }

    private void OnRemoveClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (FavoritesList.SelectedItem is QuickPanelItem item && ViewModel is { } vm)
        {
            vm.Remove(item.Favorite);
        }
    }
}
