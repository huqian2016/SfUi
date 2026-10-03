using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>データ入出力ウィンドウ（非モーダル）。</summary>
public partial class DataIoWindow : Window
{
    private readonly DataIoViewModel _viewModel;
    private bool _loaded;

    public DataIoWindow(DataIoViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnWindowLoaded;
        Closed += (_, _) => viewModel.Dispose();
    }

    public DataIoViewModel ViewModel => _viewModel;

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
