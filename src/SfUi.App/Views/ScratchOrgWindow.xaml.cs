using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>スクラッチ組織ウィンドウ（非モーダル）。</summary>
public partial class ScratchOrgWindow : Window
{
    private readonly ScratchOrgViewModel _viewModel;
    private bool _loaded;

    public ScratchOrgWindow(ScratchOrgViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnWindowLoaded;
        Closed += (_, _) => viewModel.Dispose();
    }

    public ScratchOrgViewModel ViewModel => _viewModel;

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.InitializeAsync();
    }
}
