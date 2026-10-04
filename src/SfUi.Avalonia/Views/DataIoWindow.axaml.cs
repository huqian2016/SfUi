using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>データ入出力ウィンドウ（非モーダル）。</summary>
public partial class DataIoWindow : Window
{
    private readonly DataIoViewModel _viewModel = null!;
    private bool _loaded;

    /// <summary>XAML ローダー用（実際の生成は DI 経由のコンストラクター）。</summary>
    public DataIoWindow() => AvaloniaXamlLoader.Load(this);

    public DataIoWindow(DataIoViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Opened += OnWindowOpened;
        Closed += (_, _) => _viewModel.Dispose();
    }

    public DataIoViewModel ViewModel => _viewModel;

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.LoadAsync();
    }
}
