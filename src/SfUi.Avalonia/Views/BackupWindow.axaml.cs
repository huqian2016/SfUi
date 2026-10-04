using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>レコードのバックアップと復元ウィンドウ（非モーダル）。</summary>
public partial class BackupWindow : Window
{
    private readonly BackupViewModel _viewModel = null!;
    private bool _loaded;

    /// <summary>XAML ローダー用（実際の生成は DI 経由のコンストラクター）。</summary>
    public BackupWindow() => AvaloniaXamlLoader.Load(this);

    public BackupWindow(BackupViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Opened += OnWindowOpened;
        Closed += (_, _) => _viewModel.Dispose();
    }

    public BackupViewModel ViewModel => _viewModel;

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
