using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>組織情報ウィンドウ（非モーダル。押すたびに新しいウィンドウを開く）。</summary>
public partial class OrgInfoWindow : Window
{
    private readonly OrgInfoViewModel _viewModel = null!;
    private bool _loaded;
    private bool _languageHooked;

    /// <summary>XAML ローダー用（実際の生成は DI 経由のコンストラクター）。</summary>
    public OrgInfoWindow() => AvaloniaXamlLoader.Load(this);

    public OrgInfoWindow(OrgInfoViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        this.FindControl<TextBlock>("VersionText")!.Text = $"v{typeof(OrgInfoWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

        BuildSearchColumns();
        if (!_languageHooked)
        {
            UiText.LanguageChanged += BuildSearchColumns;
            _languageHooked = true;
        }

        Opened += OnWindowOpened;
        Closed += (_, _) =>
        {
            if (_languageHooked)
            {
                UiText.LanguageChanged -= BuildSearchColumns;
                _languageHooked = false;
            }

            _viewModel.Dispose();
        };
    }

    public OrgInfoViewModel ViewModel => _viewModel;

    private async void OnWindowOpened(object? sender, EventArgs e)
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

    private void GlobalSearchBox_KeyDown(object? sender, KeyEventArgs e)
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

    private void GlobalSearchResults_DoubleTapped(object? sender, TappedEventArgs e) => ActivateSelectedSearchHit();

    private void GlobalSearchResults_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ActivateSelectedSearchHit();
            e.Handled = true;
        }
    }

    private void ActivateSelectedSearchHit()
    {
        if (this.FindControl<DataGrid>("GlobalSearchResultsGrid")?.SelectedItem is OrgInfoSearchHit hit)
        {
            _viewModel.ActivateSearchHit(hit);
        }
    }

    /// <summary>検索結果グリッドの列（ヘッダーは言語切替に追従）。</summary>
    private void BuildSearchColumns()
    {
        var grid = this.FindControl<DataGrid>("GlobalSearchResultsGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("OrgInfo_Search_ColSection"),
            Binding = new Binding(nameof(OrgInfoSearchHit.SectionTitle)),
            Width = new DataGridLength(160),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("OrgInfo_Search_ColRecord"),
            Binding = new Binding(nameof(OrgInfoSearchHit.RowSummary)),
            Width = new DataGridLength(220),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("OrgInfo_Search_ColField"),
            Binding = new Binding(nameof(OrgInfoSearchHit.FieldLabel)),
            Width = new DataGridLength(140),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("OrgInfo_Col_Value"),
            Binding = new Binding(nameof(OrgInfoSearchHit.Value)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Width = new DataGridLength(34),
            CellTemplate = new FuncDataTemplate<OrgInfoSearchHit>((_, _) =>
            {
                var button = new Button { Content = "↗", Padding = new Thickness(6, 0), FontSize = 11 };
                button.Click += (sender, _) =>
                {
                    if ((sender as Control)?.DataContext is OrgInfoSearchHit hit)
                    {
                        _viewModel.ActivateSearchHit(hit);
                    }
                };
                return button;
            }),
        });
    }
}
