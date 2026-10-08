using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
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
        RowsGrid.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnCellLinkClick));
        RowsGrid.MouseDoubleClick += OnRowDoubleClick;
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>行のダブルクリックで開く（レコード比較タブは詳細ウィンドウを開く。他は何もしない）。</summary>
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowsGrid.SelectedItem is CompareRowViewModel row)
        {
            _viewModel?.OnRowActivated(row);
            e.Handled = true;
        }
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
            Header = _viewModel.KeyHeaderText,
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
            RowsGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = table.Orgs[index].DisplayName,
                CellTemplate = BuildCellTemplate(index),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 160,
                CellStyle = CreateCellStyle(index),
            });
        }
    }

    /// <summary>セル テンプレート（テキスト + リンクがあるときだけ ↗ ボタン）。XamlReader の組み立ては
    /// {Binding} を string.Format で壊さないよう Replace で行う（リポジトリ標準の手法）。</summary>
    private static DataTemplate BuildCellTemplate(int index)
    {
        const string xaml = """
<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <DockPanel>
        <Button DockPanel.Dock="Right" Content="↗" Padding="4,0" FontSize="11" Margin="2,0,0,0"
                Tag="__IDX__" ToolTip="{Binding Cells[__IDX__].Link}">
            <Button.Style>
                <Style TargetType="Button">
                    <Setter Property="Visibility" Value="Collapsed" />
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding Cells[__IDX__].HasLink}" Value="True">
                            <Setter Property="Visibility" Value="Visible" />
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </Button.Style>
        </Button>
        <TextBlock Text="{Binding Cells[__IDX__].Text}" TextTrimming="CharacterEllipsis"
                   VerticalAlignment="Center" Margin="4,2,2,2" />
    </DockPanel>
</DataTemplate>
""";
        return (DataTemplate)XamlReader.Parse(xaml.Replace("__IDX__", index.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>セルの ↗ クリック（グリッド レベルでバブリングを受けて処理する）。</summary>
    private void OnCellLinkClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button { DataContext: CompareRowViewModel row } button &&
            button.Tag is string tag &&
            int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
            index >= 0 && index < row.Cells.Count)
        {
            var link = row.Cells[index].Link;
            if (!string.IsNullOrEmpty(link))
            {
                _viewModel?.RequestOpenLink(link!);
                e.Handled = true;
            }
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
