using System.Windows;
using System.Windows.Controls;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>オブジェクトアクセス タブ。</summary>
public partial class ObjectAccessView : UserControl
{
    private ObjectAccessViewModel? _viewModel;

    public ObjectAccessView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _viewModel = DataContext as ObjectAccessViewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.EnsureLoadedAsync();
        }
    }
}
