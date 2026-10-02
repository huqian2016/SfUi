using System.Windows.Controls;
using System.Windows.Input;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
    }

    private void Row_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow && DataContext is LogViewModel viewModel)
        {
            viewModel.FetchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
