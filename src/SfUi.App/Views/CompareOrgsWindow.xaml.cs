using System.Diagnostics;
using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>組織比較ウィンドウ（非モーダル。複数同時表示可）。</summary>
public partial class CompareOrgsWindow : Window
{
    private readonly CompareOrgsViewModel _viewModel;
    private bool _loaded;

    public CompareOrgsWindow(CompareOrgsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += OnWindowLoaded;
        Closed += (_, _) => _viewModel.Dispose();
    }

    public CompareOrgsViewModel ViewModel => _viewModel;

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            await _viewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
