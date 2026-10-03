using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>組織情報の 1 セクションを表示する（列はセクション定義から動的に生成）。</summary>
public partial class OrgInfoSectionView : UserControl
{
    /// <summary>行リンク（Setup 詳細）を開く小さなボタン列のテンプレート。</summary>
    private const string LinkButtonTemplateXaml =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
        + "<Button Content=\"↗\" Padding=\"6,0\" Margin=\"2,0\" FontSize=\"11\" IsEnabled=\"{Binding HasLink}\" "
        + "Command=\"{Binding DataContext.OpenRowLinkCommand, RelativeSource={RelativeSource AncestorType=UserControl}}\" "
        + "CommandParameter=\"{Binding}\" /></DataTemplate>";

    private bool _languageHooked;

    public OrgInfoSectionView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RebuildColumns();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_languageHooked)
        {
            UiText.LanguageChanged += OnLanguageChanged;
            _languageHooked = true;
        }

        RebuildColumns();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_languageHooked)
        {
            UiText.LanguageChanged -= OnLanguageChanged;
            _languageHooked = false;
        }
    }

    private void OnLanguageChanged() => RebuildColumns();

    private void RebuildColumns()
    {
        RowsGrid.Columns.Clear();
        if (DataContext is not OrgInfoSectionViewModel viewModel)
        {
            return;
        }

        foreach (var column in viewModel.Columns)
        {
            RowsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Label,
                Binding = new Binding($"[{column.Key}]"),
                Width = DataGridLength.Auto,
            });
        }

        if (viewModel.CanHaveLinks)
        {
            RowsGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = string.Empty,
                Width = DataGridLength.Auto,
                CellTemplate = (DataTemplate)XamlReader.Parse(LinkButtonTemplateXaml),
            });
        }
    }
}
