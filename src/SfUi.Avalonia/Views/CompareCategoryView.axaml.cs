using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SfUi.App.ViewModels;
using SfUi.Avalonia.Converters;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>比較カテゴリ 1 つのグリッド（列は組織数に応じて動的に生成）。</summary>
public partial class CompareCategoryView : UserControl
{
    private static readonly CompareCellConverter CellStyle = new();

    private CompareCategoryViewModel? _viewModel;
    private bool _languageHooked;

    public CompareCategoryView()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) => HookViewModel();
        AttachedToVisualTree += (_, _) =>
        {
            if (!_languageHooked)
            {
                UiText.LanguageChanged += RebuildColumns;
                _languageHooked = true;
            }

            RebuildColumns();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_languageHooked)
            {
                UiText.LanguageChanged -= RebuildColumns;
                _languageHooked = false;
            }
        };

        HookViewModel();
    }

    private void HookViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.ColumnsChanged -= RebuildColumns;
        }

        _viewModel = DataContext as CompareCategoryViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ColumnsChanged += RebuildColumns;
        }

        RebuildColumns();
    }

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("RowsGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        if (_viewModel is null)
        {
            return;
        }

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("Compare_KeyHeader"),
            Binding = new Binding(nameof(CompareRowViewModel.Label)),
            Width = new DataGridLength(260),
        });

        var table = _viewModel.Table;
        if (table is null)
        {
            return;
        }

        for (var i = 0; i < table.Orgs.Count; i++)
        {
            var index = i;
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = table.Orgs[index].DisplayName,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 160,
                CellTemplate = new FuncDataTemplate<CompareRowViewModel>((_, _) => BuildCell(index)),
            });
        }
    }

    /// <summary>セル表示（テキスト + ツールチップ + 状態によるグレー / 赤字 + 斜体）。</summary>
    private static TextBlock BuildCell(int index)
    {
        var text = new TextBlock
        {
            Margin = new Thickness(4, 2, 4, 2),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        text.Bind(TextBlock.TextProperty, new Binding($"Cells[{index}].Text"));
        text.Bind(ToolTip.TipProperty, new Binding($"Cells[{index}].ToolTip"));
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
