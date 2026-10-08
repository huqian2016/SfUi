using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SfUi.App.ViewModels;
using SfUi.Avalonia.Converters;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>レコード差分詳細ウィンドウ（行 = 比較項目、列 = 組織。列は組織数に応じて動的に生成）。</summary>
public partial class CompareRecordDetailWindow : Window
{
    private static readonly CompareCellConverter CellStyle = new();

    private readonly CompareRecordDetailViewModel _viewModel = null!;

    /// <summary>XAML ローダー用（実際の生成はコンストラクター経由）。</summary>
    public CompareRecordDetailWindow() => AvaloniaXamlLoader.Load(this);

    public CompareRecordDetailWindow(CompareRecordDetailViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.ColumnsChanged += RebuildColumns;
        Opened += (_, _) => RebuildColumns();
    }

    public CompareRecordDetailViewModel ViewModel => _viewModel;

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("RowsGrid");
        if (grid is null || _viewModel is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("Compare_KeyHeader"),
            Binding = new Binding(nameof(CompareRowViewModel.Label)),
            Width = new DataGridLength(280),
        });

        for (var i = 0; i < _viewModel.Orgs.Count; i++)
        {
            var index = i;
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = _viewModel.Orgs[index].DisplayName,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 160,
                CellTemplate = new FuncDataTemplate<CompareRowViewModel>((_, _) => BuildCell(index)),
            });
        }
    }

    /// <summary>セル表示（値 + ツールチップ + 状態によるグレー / 赤字 + 斜体）。</summary>
    private static Control BuildCell(int index)
    {
        var text = new TextBlock
        {
            Margin = new Thickness(4, 2, 2, 2),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Bind(TextBlock.TextProperty, new Binding($"Cells[{index}].Text"));
        text.Bind(ToolTip.TipProperty, new Binding($"Cells[{index}].Text"));
        text.Bind(TextBlock.ForegroundProperty, new Binding($"Cells[{index}]")
        {
            Converter = CellStyle,
            ConverterParameter = "foreground",
        });
        text.Bind(TextBlock.FontStyleProperty, new Binding($"Cells[{index}]")
        {
            Converter = CellStyle,
            ConverterParameter = "italic",
        });
        return text;
    }
}
