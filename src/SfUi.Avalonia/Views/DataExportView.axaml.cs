using System.ComponentModel;
using System.Data;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>データ エクスポート タブ。</summary>
public partial class DataExportView : UserControl
{
    private DataExportViewModel? _subscribed;

    public DataExportView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => Subscribe();
        Subscribe();
    }

    private void Subscribe()
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribed = DataContext as DataExportViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        }

        RebuildResults();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DataExportViewModel.ResultView))
        {
            RebuildResults();
        }
    }

    /// <summary>DataView の結果を Avalonia DataGrid の列（数値インデクサー バインド）へ変換する。</summary>
    private void RebuildResults()
    {
        var grid = this.FindControl<DataGrid>("ResultGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.ItemsSource = null;

        if (_subscribed?.ResultView is not { } view || view.Table is null)
        {
            return;
        }

        var columns = view.Table.Columns;
        var rows = new List<object?[]>(view.Count);
        foreach (DataRowView row in view)
        {
            var values = new object?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                values[i] = row.Row[i] is DBNull ? null : row.Row[i];
            }

            rows.Add(values);
        }

        for (var i = 0; i < columns.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i].ColumnName,
                Binding = new Binding($"[{i}]"),
                Width = new DataGridLength(140),
            });
        }

        grid.ItemsSource = rows;
    }
}
