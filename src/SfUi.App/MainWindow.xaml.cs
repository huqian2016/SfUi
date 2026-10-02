using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppLog _log;

    public MainWindow(MainViewModel viewModel, AppPaths paths, AppLog log)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _log = log;
        DataContext = viewModel;

        DataPathText.Text = $"データ: {paths.DataRoot}";
        VersionText.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

        Loaded += OnWindowLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>グローバルショートカット: Ctrl+1..9（クイックパネル実行）/ F5（直前の操作を再実行）。</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
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

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _log.Info("MainWindow シェル表示完了");
        await _viewModel.InitializeAsync();
    }

    private void FolderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderComboBox.SelectedItem is RecentFolder folder)
        {
            _viewModel.CommitFolder(folder.Path);
        }
    }

    private void FolderComboBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.CommitFolder(_viewModel.SelectedFolder);
            e.Handled = true;
        }
    }

    private void FolderComboBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.CommitFolder(_viewModel.SelectedFolder);
    }
}
