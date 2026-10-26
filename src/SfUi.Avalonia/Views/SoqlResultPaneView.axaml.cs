using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>
/// 複数 SOQL 実行時の結果枠 1 つ分のビュー。列は枠ごとの結果に合わせてコードで構築する。
/// </summary>
public partial class SoqlResultPaneView : UserControl
{
    private SoqlResultPaneViewModel? _subscribed;

    private SoqlResultPaneViewModel? ViewModel => DataContext as SoqlResultPaneViewModel;

    public SoqlResultPaneView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => Subscribe();
    }

    private void Subscribe()
    {
        if (_subscribed is not null)
        {
            _subscribed.Columns.CollectionChanged -= OnColumnsChanged;
        }

        _subscribed = ViewModel;
        if (_subscribed is not null)
        {
            _subscribed.Columns.CollectionChanged += OnColumnsChanged;
        }

        RebuildColumns();
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildColumns();

    /// <summary>結果の列に合わせてグリッドの列を作り直す（Id = レコードリンク / それ以外 = 読み取り専用）。</summary>
    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("PaneGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        for (var i = 0; i < viewModel.Columns.Count; i++)
        {
            var index = i;
            var column = viewModel.Columns[index];
            if (column.IsId)
            {
                grid.Columns.Add(new DataGridTemplateColumn
                {
                    Header = column.Key,
                    Width = new DataGridLength(170),
                    CellTemplate = new FuncDataTemplate<SoqlGridRow>((_, _) => BuildIdCell()),
                });
                continue;
            }

            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = column.Key,
                Width = new DataGridLength(160),
                CellTemplate = new FuncDataTemplate<SoqlGridRow>((_, _) => BuildReadOnlyCell(index)),
            });
        }
    }

    /// <summary>読み取り専用セル（テキスト表示）。</summary>
    private static Control BuildReadOnlyCell(int index)
    {
        var text = new TextBlock
        {
            Margin = new Thickness(4, 2),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        text.Bind(TextBlock.TextProperty, new Binding($"Cells[{index}].Value"));
        text.Bind(ToolTip.TipProperty, new Binding($"Cells[{index}].Error"));
        return text;
    }

    /// <summary>Id セル（クリックでレコードページを開くリンク）。</summary>
    private Control BuildIdCell()
    {
        var openRecord = UiText.T("Soql_OpenRecord");
        var button = new Button
        {
            Padding = new Thickness(4, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#1B6ACB")),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.Bind(ContentControl.ContentProperty, new Binding(nameof(SoqlGridRow.RecordId)));
        ToolTip.SetTip(button, openRecord);
        AutomationProperties.SetName(button, openRecord);
        button.Click += (sender, _) =>
        {
            if ((sender as Control)?.DataContext is SoqlGridRow row)
            {
                ViewModel?.OpenRecordCommand.Execute(row);
            }
        };
        return button;
    }
}
