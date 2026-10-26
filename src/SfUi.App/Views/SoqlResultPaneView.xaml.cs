using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>
/// 複数 SOQL 実行時の結果枠 1 つ分のビュー。列は枠ごとの結果に合わせてコードで構築する。
/// </summary>
public partial class SoqlResultPaneView : UserControl
{
    private const string ReadOnlyCellTemplateXaml =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
        + "<TextBlock Text=\"{Binding Cells[__I__].Value}\" Margin=\"4,2\" VerticalAlignment=\"Center\" "
        + "TextTrimming=\"CharacterEllipsis\" ToolTip=\"{Binding Cells[__I__].Error}\" /></DataTemplate>";

    private const string IdCellTemplateXaml =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
        + "<Button Content=\"{Binding RecordId}\" Padding=\"4,2\" Margin=\"2,0\" HorizontalAlignment=\"Left\" "
        + "Background=\"Transparent\" BorderThickness=\"0\" Foreground=\"#1B6ACB\" Cursor=\"Hand\" "
        + "ToolTip=\"__NAME__\" AutomationProperties.Name=\"__NAME__\" "
        + "Command=\"{Binding DataContext.OpenRecordCommand, RelativeSource={RelativeSource AncestorType=UserControl}}\" "
        + "CommandParameter=\"{Binding}\" /></DataTemplate>";

    private SoqlResultPaneViewModel? _subscribed;

    private SoqlResultPaneViewModel? ViewModel => DataContext as SoqlResultPaneViewModel;

    public SoqlResultPaneView()
    {
        InitializeComponent();
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
        PaneGrid.Columns.Clear();
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var openRecord = UiText.T("Soql_OpenRecord");
        for (var i = 0; i < viewModel.Columns.Count; i++)
        {
            var index = i;
            var column = viewModel.Columns[index];
            if (column.IsId)
            {
                PaneGrid.Columns.Add(new DataGridTemplateColumn
                {
                    Header = column.Key,
                    Width = new DataGridLength(170),
                    CellTemplate = (DataTemplate)XamlReader.Parse(IdCellTemplateXaml.Replace("__NAME__", openRecord)),
                });
                continue;
            }

            PaneGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = column.Key,
                Width = new DataGridLength(160),
                CellTemplate = (DataTemplate)XamlReader.Parse(ReadOnlyCellTemplateXaml.Replace("__I__", index.ToString())),
            });
        }
    }
}
