using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>比較カテゴリ 1 つのグリッド（列は組織数に応じて動的に生成）。</summary>
public partial class CompareCategoryView : UserControl
{
    private CompareCategoryViewModel? _viewModel;
    private bool _languageHooked;

    public CompareCategoryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_languageHooked)
        {
            UiText.LanguageChanged += RebuildColumns;
            _languageHooked = true;
        }

        RebuildColumns();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_languageHooked)
        {
            UiText.LanguageChanged -= RebuildColumns;
            _languageHooked = false;
        }
    }

    private void RebuildColumns()
    {
        RowsGrid.Columns.Clear();
        if (_viewModel is null)
        {
            return;
        }

        RowsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("Compare_KeyHeader"),
            Binding = new Binding(nameof(CompareRowViewModel.Label)),
            Width = new DataGridLength(260),
            ElementStyle = CreateCellTextStyle(),
        });

        var table = _viewModel.Table;
        if (table is null)
        {
            return;
        }

        for (var i = 0; i < table.Orgs.Count; i++)
        {
            var index = i;
            RowsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = table.Orgs[index].DisplayName,
                Binding = new Binding($"Cells[{index}].Text"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 160,
                ElementStyle = CreateCellTextStyle(),
                CellStyle = CreateCellStyle(index),
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

    private static Style CreateCellStyle(int index)
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding($"Cells[{index}].ToolTip")));

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
