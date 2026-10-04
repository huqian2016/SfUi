using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>組織情報の 1 セクションを表示する（列はセクション定義から動的に生成）。</summary>
public partial class OrgInfoSectionView : UserControl
{
    private bool _languageHooked;

    public OrgInfoSectionView()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) => RebuildColumns();
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
    }

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("RowsGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        if (DataContext is not OrgInfoSectionViewModel viewModel)
        {
            return;
        }

        foreach (var column in viewModel.Columns)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Label,
                Binding = new Binding($"[{column.Key}]"),
                Width = DataGridLength.Auto,
            });
        }

        if (viewModel.CanHaveLinks)
        {
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = string.Empty,
                Width = DataGridLength.Auto,
                CellTemplate = new FuncDataTemplate<OrgInfoRowView>((_, _) => BuildLinkButton(viewModel)),
            });
        }
    }

    /// <summary>行リンク（Setup 詳細）を開く小さなボタン。</summary>
    private static Button BuildLinkButton(OrgInfoSectionViewModel viewModel)
    {
        var button = new Button
        {
            Content = "↗",
            Padding = new Thickness(6, 0),
            Margin = new Thickness(2, 0),
            FontSize = 11,
        };
        button.Bind(IsEnabledProperty, new Binding(nameof(OrgInfoRowView.HasLink)));
        button.Click += (sender, _) =>
        {
            if ((sender as Control)?.DataContext is OrgInfoRowView row)
            {
                viewModel.OpenRowLinkCommand.Execute(row);
            }
        };
        return button;
    }
}
