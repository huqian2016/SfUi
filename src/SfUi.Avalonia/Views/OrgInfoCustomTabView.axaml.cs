using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>カスタムタブ（マイ設定で定義した項目の集約表示）。</summary>
public partial class OrgInfoCustomTabView : UserControl
{
    private bool _languageHooked;

    public OrgInfoCustomTabView()
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
        var grid = this.FindControl<DataGrid>("ItemsGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("OrgInfo_Col_Item"),
            Binding = new Binding(nameof(CustomItemView.Label)),
            Width = new DataGridLength(220),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("OrgInfo_Col_Value"),
            Binding = new Binding(nameof(CustomItemView.Value)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("OrgInfo_Col_Source"),
            Binding = new Binding(nameof(CustomItemView.SourceName)),
            Width = new DataGridLength(150),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("OrgInfo_Col_FetchedAt"),
            Binding = new Binding(nameof(CustomItemView.FetchedAtText)),
            Width = new DataGridLength(180),
        });

        if (DataContext is OrgInfoCustomTabViewModel viewModel)
        {
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Width = new DataGridLength(34),
                CellTemplate = new FuncDataTemplate<CustomItemView>((_, _) => BuildLinkButton(viewModel)),
            });
        }
    }

    private static Button BuildLinkButton(OrgInfoCustomTabViewModel viewModel)
    {
        var button = new Button
        {
            Content = "↗",
            Padding = new Thickness(6, 0),
            FontSize = 11,
        };
        button.Bind(IsEnabledProperty, new Binding(nameof(CustomItemView.HasLink)));
        button.Click += (sender, _) =>
        {
            if ((sender as Control)?.DataContext is CustomItemView item)
            {
                viewModel.OpenLinkCommand.Execute(item);
            }
        };
        return button;
    }
}
