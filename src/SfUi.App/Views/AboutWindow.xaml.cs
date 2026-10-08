using System.Windows;
using System.Windows.Documents;
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

        // マニュアル リンクは現在の UI 言語の先頭ページを指す（例: ja → docs/manual/ja.md）
        var manualUrl = ManualLinks.BuildManualUrl();
        ManualLink.NavigateUri = new Uri(manualUrl);
        ManualLink.Inlines.Clear();
        ManualLink.Inlines.Add(new Run(manualUrl));
    }

    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        UrlLauncher.TryOpen(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
