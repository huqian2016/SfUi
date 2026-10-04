using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>組織比較ウィンドウ（非モーダル。複数同時表示可）。</summary>
public partial class CompareOrgsWindow : Window
{
    private readonly CompareOrgsViewModel _viewModel = null!;
    private bool _loaded;

    /// <summary>XAML ローダー用（実際の生成は DI 経由のコンストラクター）。</summary>
    public CompareOrgsWindow() => AvaloniaXamlLoader.Load(this);

    public CompareOrgsWindow(CompareOrgsViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        Opened += OnWindowOpened;
        Closed += (_, _) => _viewModel.Dispose();
    }

    public CompareOrgsViewModel ViewModel => _viewModel;

    private async void OnWindowOpened(object? sender, EventArgs e)
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
