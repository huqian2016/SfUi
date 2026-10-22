using System.Windows;
using System.Windows.Controls;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>ユーザー中心の実効アクセス タブ（ユーザー複数選択 + オブジェクト / 項目の実効権限）。</summary>
public partial class UserAccessView : UserControl
{
    private UserAccessViewModel? _viewModel;
    private bool _ensureLoaded;

    public UserAccessView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _viewModel = DataContext as UserAccessViewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_ensureLoaded || _viewModel is null)
        {
            return;
        }

        _ensureLoaded = true;
        await _viewModel.EnsureLoadedAsync();
    }
}
