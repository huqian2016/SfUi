using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>バックアップ内のレコードを表示する別ウィンドウ（列はバックアップ内容から動的に生成）。</summary>
public partial class BackupRecordsWindow : Window
{
    private readonly BackupRecordsViewModel _viewModel;
    private bool _loaded;

    public BackupRecordsWindow(BackupRecordsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.ColumnsChanged += RebuildColumns;
        Loaded += OnWindowLoaded;
        Closed += (_, _) => viewModel.ColumnsChanged -= RebuildColumns;
    }

    public BackupRecordsViewModel ViewModel => _viewModel;

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.LoadAsync();
    }

    private void RebuildColumns()
    {
        RecordsGrid.Columns.Clear();
        var columns = _viewModel.Columns;
        for (var i = 0; i < columns.Count; i++)
        {
            RecordsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i],
                Binding = new Binding($"Cells[{i}]"),
                Width = i == 0 ? new DataGridLength(200) : new DataGridLength(180),
                ElementStyle = CreateCellTextStyle(),
            });
        }
    }

    private static Style CreateCellTextStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2)));
        return style;
    }
}
