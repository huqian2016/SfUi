using System.Windows;
using System.Windows.Controls;

namespace SfUi.App.Views;

/// <summary>簡易テキスト入力ダイアログ（WPF に InputBox が無いため）。</summary>
public static class InputBox
{
    public static string? Show(string title, string prompt, string defaultValue = "")
    {
        var owner = Application.Current?.MainWindow;
        var window = new Window
        {
            Title = title,
            Width = 560,
            Height = 180,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        if (owner is not null)
        {
            window.Owner = owner;
        }

        var panel = new DockPanel { Margin = new Thickness(12) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var okButton = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "キャンセル", IsCancel = true, Width = 90 };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        var textBox = new TextBox
        {
            Text = defaultValue,
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0),
        };
        DockPanel.SetDock(textBox, Dock.Bottom);
        panel.Children.Add(textBox);

        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        window.Content = panel;

        okButton.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        return window.ShowDialog() == true ? textBox.Text : null;
    }
}
