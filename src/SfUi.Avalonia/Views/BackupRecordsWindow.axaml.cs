using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>バックアップ内のレコードを表示する別ウィンドウ（列はバックアップ内容から動的に生成）。</summary>
public partial class BackupRecordsWindow : Window
{
    private readonly BackupRecordsViewModel _viewModel = null!;
    private bool _loaded;

    /// <summary>XAML ローダー用（実際の生成は DI 経由のコンストラクター）。</summary>
    public BackupRecordsWindow() => AvaloniaXamlLoader.Load(this);

    public BackupRecordsWindow(BackupRecordsViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.ColumnsChanged += RebuildColumns;

        Opened += OnWindowOpened;
        Closed += (_, _) => viewModel.ColumnsChanged -= RebuildColumns;
    }

    public BackupRecordsViewModel ViewModel => _viewModel;

    private async void OnWindowOpened(object? sender, EventArgs e)
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
        var grid = this.FindControl<DataGrid>("RecordsGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        var openRecord = UiText.T("BackupRecords_OpenRecord");
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = openRecord,
            Width = new DataGridLength(60),
            CellTemplate = new FuncDataTemplate<BackupRecordRowViewModel>((_, _) => BuildLinkButton(openRecord)),
        });

        var columns = _viewModel.Columns;
        for (var i = 0; i < columns.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i],
                Binding = new Binding($"Cells[{i}]"),
                Width = new DataGridLength(i == 0 ? 200 : 180),
            });
        }
    }

    /// <summary>レコード ページを開く小さなボタン。</summary>
    private Button BuildLinkButton(string tooltip)
    {
        var button = new Button
        {
            Content = "↗",
            Padding = new Thickness(6, 0),
            Margin = new Thickness(2, 0),
            FontSize = 11,
        };
        ToolTip.SetTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        button.Bind(IsEnabledProperty, new Binding(nameof(BackupRecordRowViewModel.HasLink)));
        button.Click += (sender, _) =>
        {
            if ((sender as Control)?.DataContext is BackupRecordRowViewModel row)
            {
                _viewModel.OpenRecordCommand.Execute(row);
            }
        };
        return button;
    }
}
