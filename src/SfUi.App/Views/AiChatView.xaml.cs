using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

public partial class AiChatView : UserControl
{
    public AiChatView()
    {
        InitializeComponent();

        // Ctrl+Enter で送信（Enter 単独は改行）
        InputBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _ = (DataContext as AiChatViewModel)?.SendCommand.ExecuteAsync(null);
                e.Handled = true;
            }
        };

        Loaded += (_, _) =>
        {
            if (DataContext is AiChatViewModel viewModel)
            {
                viewModel.Messages.CollectionChanged += (_, _) =>
                    Dispatcher.BeginInvoke(() => MessagesScroll.ScrollToEnd(), DispatcherPriority.Background);
            }
        };
    }
}
