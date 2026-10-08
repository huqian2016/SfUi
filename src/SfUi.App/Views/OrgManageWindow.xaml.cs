using System.Windows;
using System.Windows.Controls;
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

    /// <summary>アクセス トークン（PasswordBox は直接バインドできないため手動同期）。</summary>
    private void RegisterTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrgManageViewModel viewModel && sender is PasswordBox box)
        {
            viewModel.RegisterAccessToken = box.Password;
        }
    }

    /// <summary>複数選択を ViewModel へ反映する（一括操作の対象）。</summary>
    private void OrgsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid || DataContext is not OrgManageViewModel viewModel)
        {
            return;
        }

        var rows = new List<OrgManageOrgRowViewModel>();
        foreach (var item in grid.SelectedItems)
        {
            if (item is OrgManageOrgRowViewModel row)
            {
                rows.Add(row);
            }
        }

        viewModel.SetSelectedOrgRows(rows);
    }
}
