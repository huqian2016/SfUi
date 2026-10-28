using Avalonia.Controls;
using Avalonia.Data;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>ローカル レポート ウィンドウ（Avalonia）。</summary>
public partial class ReportsWindow : Window
{
    private IReadOnlyList<string> _builtColumns = Array.Empty<string>();

    public ReportsWindow()
    {
        InitializeComponent();
    }

    public ReportsWindow(ReportsViewModel viewModel)
        : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReportsViewModel.GridColumns))
            {
                RebuildColumns();
            }
        };

        RebuildColumns();
        Closed += (_, _) => viewModel.Dispose();
    }

    public ReportsViewModel ViewModel { get; } = null!;

    /// <summary>結果の列数・列名に合わせて DataGrid の列を作り直す（列は結果ごとに変わるため）。</summary>
    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("ResultGrid");
        if (grid is null || ViewModel is null)
        {
            return;
        }

        var columns = ViewModel.GridColumns;
        if (columns.Count == _builtColumns.Count
            && columns.Zip(_builtColumns, (a, b) => string.Equals(a, b, StringComparison.Ordinal)).All(same => same))
        {
            return;
        }

        grid.Columns.Clear();
        for (var i = 0; i < columns.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i],
                Binding = new Binding($"[{i}]") { Mode = BindingMode.OneWay },
                Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
            });
        }

        _builtColumns = columns.ToList();
    }
}
