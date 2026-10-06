using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = null!;
    private readonly AppLog _log = null!;

    /// <summary>XAML ローダー用（実際の生成は DI 経由のコンストラクター）。</summary>
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    public MainWindow(MainViewModel viewModel, AppPaths paths, AppLog log) : this()
    {
        _viewModel = viewModel;
        _log = log;
        DataContext = viewModel;

        this.FindControl<TextBlock>("DataPathText")!.Text = UiText.T("Main_DataPathFmt", paths.DataRoot);
        this.FindControl<TextBlock>("VersionText")!.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

        Opened += OnWindowOpened;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>グローバルショートカット: Ctrl(/Cmd)+1..9（クイックパネル実行）/ F5（直前の操作を再実行）。</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            var number = e.Key switch
            {
                >= Key.D1 and <= Key.D9 => (int)e.Key - (int)Key.D0,
                >= Key.NumPad1 and <= Key.NumPad9 => (int)e.Key - (int)Key.NumPad0,
                _ => 0,
            };
            if (number is >= 1 and <= 9)
            {
                _viewModel.ExecuteQuickSlot(number);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.F5)
        {
            _viewModel.ReplayLastOperation();
            e.Handled = true;
        }
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        _log.Info("MainWindow シェル表示完了");
        await _viewModel.InitializeAsync();
    }

    private void FolderComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.FindControl<AutoCompleteBox>("FolderComboBox")?.SelectedItem is RecentFolder folder)
        {
            _viewModel.CommitFolder(folder.Path);
        }
    }

    private void FolderComboBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.CommitFolder(_viewModel.SelectedFolder);
            e.Handled = true;
        }
    }

    private void FolderComboBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        _viewModel.CommitFolder(_viewModel.SelectedFolder);
    }

    private void AboutButton_Click(object? sender, RoutedEventArgs e)
    {
        _ = new Views.AboutWindow().ShowDialog(this);
    }
}
