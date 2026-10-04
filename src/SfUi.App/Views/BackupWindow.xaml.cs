using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>レコードのバックアップと復元ウィンドウ（非モーダル）。</summary>
public partial class BackupWindow : Window
{
    private readonly BackupViewModel _viewModel;
    private bool _loaded;

    public BackupWindow(BackupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnWindowLoaded;
        Closed += (_, _) => viewModel.Dispose();
    }

    public BackupViewModel ViewModel => _viewModel;

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.LoadAsync();
    }
}
