using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>組織管理ウィンドウ（組織一覧の管理・ヘルス・移行棚卸し）。非モーダル。</summary>
public partial class OrgManageWindow : Window
{
    private readonly OrgManageViewModel _viewModel;
    private bool _loaded;

    public OrgManageWindow(OrgManageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnWindowLoaded;
        Closed += (_, _) => viewModel.Dispose();
    }

    public OrgManageViewModel ViewModel => _viewModel;

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
