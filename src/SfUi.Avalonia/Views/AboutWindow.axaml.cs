using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>バージョン情報・リンク・連絡先を表示する About ウィンドウ。</summary>
public partial class AboutWindow : Window
{
    /// <summary>XAML ローダー用。</summary>
    public AboutWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("VersionText")!.Text = $"v{typeof(AboutWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

        // マニュアル リンクは現在の UI 言語の先頭ページを指す（例: ja → docs/manual/ja.md）
        this.FindControl<Button>("ManualLinkButton")!.Content = ManualLinks.BuildManualUrl();
    }

    private void OnRepoClick(object? sender, RoutedEventArgs e) => UrlLauncher.TryOpen("https://github.com/huqian2016/SfUi");

    private void OnManualClick(object? sender, RoutedEventArgs e) => UrlLauncher.TryOpen(ManualLinks.BuildManualUrl());

    private void OnIssuesClick(object? sender, RoutedEventArgs e) => UrlLauncher.TryOpen("https://github.com/huqian2016/SfUi/issues");

    private void OnContactClick(object? sender, RoutedEventArgs e) => UrlLauncher.TryOpen("mailto:ko@hks-tech-kk.com");

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
