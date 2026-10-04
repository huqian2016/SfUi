using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

public partial class CommandView : UserControl
{
    public CommandView()
    {
        AvaloniaXamlLoader.Load(this);

        // Enter で実行（単一行入力）
        this.FindControl<TextBox>("CommandInput")!.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                (DataContext as CommandViewModel)?.ExecuteCommand.Execute(null);
                e.Handled = true;
            }
        };
    }
}
