using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>組織情報ウィンドウ（非モーダル。押すたびに新しいウィンドウを開く）。</summary>
public partial class OrgInfoWindow : Window
{
    private readonly OrgInfoViewModel _viewModel;
    private bool _loaded;

    public OrgInfoWindow(OrgInfoViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        VersionText.Text = $"v{typeof(OrgInfoWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

        Loaded += OnWindowLoaded;
        Closed += (_, _) => _viewModel.Dispose();
    }

    public OrgInfoViewModel ViewModel => _viewModel;

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
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

    private void GlobalSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.RunGlobalSearchNow();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void GlobalSearchResults_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ActivateSelectedSearchHit();

    private void GlobalSearchResults_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ActivateSelectedSearchHit();
            e.Handled = true;
        }
    }

    private void ActivateSelectedSearchHit()
    {
        if (GlobalSearchResultsGrid.SelectedItem is OrgInfoSearchHit hit)
        {
            _viewModel.ActivateSearchHit(hit);
        }
    }
}
