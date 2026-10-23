using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>スクラッチ組織ウィンドウ（非モーダル）。</summary>
public partial class ScratchOrgWindow : Window
{
    private readonly ScratchOrgViewModel _viewModel = null!;
    private bool _loaded;

    /// <summary>XAML ローダー用（実際の生成は DI 経由のコンストラクター）。</summary>
    public ScratchOrgWindow() => AvaloniaXamlLoader.Load(this);

    public ScratchOrgWindow(ScratchOrgViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Opened += OnWindowOpened;
        Closed += (_, _) => _viewModel.Dispose();
    }

    public ScratchOrgViewModel ViewModel => _viewModel;

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.InitializeAsync();
    }
}
