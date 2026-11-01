using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>接続アプリ（ConnectedApp / ExternalClientApplication）ウィンドウ。</summary>
public partial class ConnectedAppsWindow : Window
{
    public ConnectedAppsWindow(ConnectedAppsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public ConnectedAppsViewModel ViewModel { get; }
}
