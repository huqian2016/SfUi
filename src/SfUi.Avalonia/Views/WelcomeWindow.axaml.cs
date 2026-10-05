using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>ようこそ画面（機能紹介 + Salesforce CLI の検出 / インストール案内）。モーダルで表示する。</summary>
public partial class WelcomeWindow : Window
{
    private readonly WelcomeViewModel _viewModel = null!;

    /// <summary>XAML ローダー用（実際の生成は DI 経由のコンストラクター）。</summary>
    public WelcomeWindow() => AvaloniaXamlLoader.Load(this);

    public WelcomeWindow(WelcomeViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
    }

    public WelcomeViewModel ViewModel => _viewModel;
}
