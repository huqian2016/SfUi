using System.Windows;
using System.Windows.Navigation;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>バージョン情報・リンク・連絡先を表示する About ウィンドウ。</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{typeof(AboutWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";
    }

    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        UrlLauncher.TryOpen(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
