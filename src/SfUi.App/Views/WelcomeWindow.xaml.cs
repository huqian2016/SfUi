using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>ようこそ画面（機能紹介 + Salesforce CLI の検出 / インストール案内）。モーダルで表示する。</summary>
public partial class WelcomeWindow : Window
{
    private readonly WelcomeViewModel _viewModel;

    public WelcomeWindow(WelcomeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
    }

    public WelcomeViewModel ViewModel => _viewModel;
}
