using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>レコード差分詳細ウィンドウ（行 = 比較項目、列 = 組織。列は組織数に応じて動的に生成）。</summary>
public partial class CompareRecordDetailWindow : Window
{
    private readonly CompareRecordDetailViewModel _viewModel;

    public CompareRecordDetailWindow(CompareRecordDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.ColumnsChanged += RebuildColumns;
        RebuildColumns();
    }

    public CompareRecordDetailViewModel ViewModel => _viewModel;

    private void RebuildColumns()
    {
        RowsGrid.Columns.Clear();
        RowsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("Compare_KeyHeader"),
            Binding = new Binding(nameof(CompareRowViewModel.Label)),
            Width = new DataGridLength(280),
            ElementStyle = CreateFieldStyle(),
        });

        for (var i = 0; i < _viewModel.Orgs.Count; i++)
        {
            var index = i;
            RowsGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = _viewModel.Orgs[index].DisplayName,
                CellTemplate = BuildCellTemplate(index),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 160,
                CellStyle = CreateCellStyle(index),
            });
        }
    }

    /// <summary>セル テンプレート（値のみ。ツールチップで全文を表示。XamlReader の組み立ては Replace で行う）。</summary>
    private static DataTemplate BuildCellTemplate(int index)
    {
        const string xaml = """
<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <TextBlock Text="{Binding Cells[__IDX__].Text}" TextTrimming="CharacterEllipsis"
               ToolTip="{Binding Cells[__IDX__].Text}"
               VerticalAlignment="Center" Margin="4,2,2,2" />
</DataTemplate>
""";
        return (DataTemplate)XamlReader.Parse(xaml.Replace("__IDX__", index.ToString(CultureInfo.InvariantCulture)));
    }

    private static Style CreateFieldStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2)));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("Key")));
        return style;
    }

    private static Style CreateCellStyle(int index)
    {
        var style = new Style(typeof(DataGridCell));

        var missing = new DataTrigger { Binding = new Binding($"Cells[{index}].IsMissing"), Value = true };
        missing.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Gray));
        missing.Setters.Add(new Setter(Control.FontStyleProperty, FontStyles.Italic));
        style.Triggers.Add(missing);

        var notFetched = new DataTrigger { Binding = new Binding($"Cells[{index}].IsNotFetched"), Value = true };
        notFetched.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Gray));
        notFetched.Setters.Add(new Setter(Control.FontStyleProperty, FontStyles.Italic));
        style.Triggers.Add(notFetched);

        var failed = new DataTrigger { Binding = new Binding($"Cells[{index}].IsFailed"), Value = true };
        failed.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xB0, 0x00, 0x20))));
        style.Triggers.Add(failed);

        return style;
    }
}
