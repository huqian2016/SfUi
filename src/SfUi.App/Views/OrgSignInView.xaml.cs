using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>
/// 内部ブラウザー（WebView2）で Salesforce にサインインし、組織を登録するタブ。
/// ログイン後のセッション Cookie（sid）を検出して ViewModel へ渡す。
/// </summary>
public partial class OrgSignInView : UserControl
{
    private bool _initialized;

    public OrgSignInView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private OrgSignInViewModel? ViewModel => DataContext as OrgSignInViewModel;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            // ユーザー データは LocalApplicationData 配下に置く（MSIX でも書き込み可能）
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SfUi",
                "webview2");
            Directory.CreateDirectory(dataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
            await Browser.EnsureCoreWebView2Async(environment);

            var core = Browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.SourceChanged += async (_, _) => await DetectSessionAsync();
            core.NavigationCompleted += async (_, _) => await DetectSessionAsync();

            Navigate();
        }
        catch (Exception ex)
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.StatusText = UiText.T("OrgSignIn_WebViewFailedFmt", ex.Message);
            }
        }
    }

    /// <summary>入力された URL へ移動する。</summary>
    private void Navigate()
    {
        if (Browser.CoreWebView2 is null || ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.ClearSession();
        var target = OrgSignInUrl.Normalize(viewModel.InstanceUrl);
        Browser.CoreWebView2.Navigate(target);
    }

    /// <summary>ログイン後のページから組織 URL とセッションを検出する。</summary>
    private async Task DetectSessionAsync()
    {
        if (Browser.CoreWebView2 is not { } core || ViewModel is not { } viewModel)
        {
            return;
        }

        try
        {
            if (!OrgSignInUrl.TryGetInstanceUrl(core.Source, out var instanceUrl))
            {
                return;
            }

            var cookies = await core.CookieManager.GetCookiesAsync(instanceUrl);
            var sid = OrgSignInUrl.FindSessionCookie(cookies.Select(c => (c.Name, c.Value)));
            if (sid is not null)
            {
                viewModel.ApplySession(instanceUrl, sid);
            }
        }
        catch (Exception)
        {
            // 検出の失敗はサインイン操作を妨げない
        }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => Navigate();

    private void OnReloadClick(object sender, RoutedEventArgs e) => Browser.CoreWebView2?.Reload();
}
