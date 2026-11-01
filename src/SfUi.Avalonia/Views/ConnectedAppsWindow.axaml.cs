using Avalonia.Controls;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>接続アプリ（ConnectedApp / ExternalClientApplication）ウィンドウ（Avalonia）。</summary>
public partial class ConnectedAppsWindow : Window
{
    public ConnectedAppsWindow()
    {
        InitializeComponent();
    }

    public ConnectedAppsWindow(ConnectedAppsViewModel viewModel)
        : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public ConnectedAppsViewModel? ViewModel { get; }
}
