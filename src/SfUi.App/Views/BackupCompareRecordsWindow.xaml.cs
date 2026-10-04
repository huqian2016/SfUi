using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>バックアップ比較のレコード単位差分を表示する別ウィンドウ。</summary>
public partial class BackupCompareRecordsWindow : Window
{
    private readonly BackupCompareRecordsViewModel _viewModel;
    private bool _loaded;

    public BackupCompareRecordsWindow(BackupCompareRecordsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnWindowLoaded;
    }

    public BackupCompareRecordsViewModel ViewModel => _viewModel;

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
