using System.Windows;
using SfUi.App.Views;
using SfUi.Presentation;

namespace SfUi.App.Services;

/// <summary>WPF の MessageBox / InputBox を使う IDialogService 実装。</summary>
public sealed class WpfDialogService : IDialogService
{
    public void Info(string message, string caption) =>
        Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Warning(string message, string caption) =>
        Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string message, string caption) =>
        Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public bool ConfirmDestructive(string message, string caption) =>
        Show(message, caption, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    public string? Prompt(string title, string prompt, string defaultValue = "") =>
        InputBox.Show(title, prompt, defaultValue);

    private static MessageBoxResult Show(
        string message,
        string caption,
        MessageBoxButton button,
        MessageBoxImage image,
        MessageBoxResult defaultResult = MessageBoxResult.OK)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        return owner is null
            ? MessageBox.Show(message, caption, button, image, defaultResult)
            : MessageBox.Show(owner, message, caption, button, image, defaultResult);
    }
}
