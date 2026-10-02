using System.Windows.Controls;
using System.Windows.Input;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

public partial class CommandView : UserControl
{
    public CommandView()
    {
        InitializeComponent();

        // Enter で実行（単一行入力）
        CommandInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _ = (DataContext as CommandViewModel)?.ExecuteCommand.ExecuteAsync(null);
                e.Handled = true;
            }
        };
    }
}
