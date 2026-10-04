using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

public partial class AiChatView : UserControl
{
    public AiChatView()
    {
        AvaloniaXamlLoader.Load(this);

        // Ctrl+Enter で送信（Enter 単独は改行）
        this.FindControl<TextBox>("InputBox")!.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                (DataContext as AiChatViewModel)?.SendCommand.Execute(null);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        Loaded += (_, _) =>
        {
            if (DataContext is AiChatViewModel viewModel)
            {
                viewModel.Messages.CollectionChanged += (_, _) =>
                    Dispatcher.UIThread.Post(() => this.FindControl<ScrollViewer>("MessagesScroll")?.ScrollToEnd(), DispatcherPriority.Background);
            }
        };
    }
}
