using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>データ インポート タブ。</summary>
public partial class DataImportView : UserControl
{
    private bool _languageHooked;

    public DataImportView()
    {
        AvaloniaXamlLoader.Load(this);

        BuildColumns();
        AttachedToVisualTree += (_, _) =>
        {
            if (!_languageHooked)
            {
                UiText.LanguageChanged += BuildColumns;
                _languageHooked = true;
            }

            BuildColumns();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_languageHooked)
            {
                UiText.LanguageChanged -= BuildColumns;
                _languageHooked = false;
            }
        };
    }

    private void BuildColumns()
    {
        BuildMappingColumns();
        BuildResultColumns();
    }

    private void BuildMappingColumns()
    {
        var grid = this.FindControl<DataGrid>("MappingGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiText.T("DataImport_MapUse"),
            Width = new DataGridLength(52),
            CellTemplate = new FuncDataTemplate<ImportMappingRowViewModel>((_, _) =>
            {
                var check = new CheckBox();
                check.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(ImportMappingRowViewModel.Include)) { Mode = BindingMode.TwoWay });
                return check;
            }),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("DataImport_MapColumn"),
            Binding = new Binding(nameof(ImportMappingRowViewModel.CsvColumn)),
            Width = new DataGridLength(170),
            IsReadOnly = true,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("DataImport_MapSample"),
            Binding = new Binding(nameof(ImportMappingRowViewModel.Sample)),
            Width = new DataGridLength(220),
            IsReadOnly = true,
        });
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiText.T("DataImport_MapField"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<ImportMappingRowViewModel>((_, _) =>
            {
                var combo = new ComboBox();
                combo.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(ImportMappingRowViewModel.FieldOptions)));
                combo.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(ImportMappingRowViewModel.SelectedField)) { Mode = BindingMode.TwoWay });
                combo.ItemTemplate = new FuncDataTemplate<object>((item, _) =>
                {
                    var text = new TextBlock();
                    text.Bind(TextBlock.TextProperty, new Binding("Display"));
                    return text;
                });
                return combo;
            }),
        });
    }

    private void BuildResultColumns()
    {
        var grid = this.FindControl<DataGrid>("ImportResultGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("DataImport_ResultRow"),
            Binding = new Binding(nameof(ImportResultRowViewModel.RowText)),
            Width = new DataGridLength(64),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("DataImport_ResultStatus"),
            Binding = new Binding(nameof(ImportResultRowViewModel.StatusText)),
            Width = new DataGridLength(88),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("DataImport_ResultId"),
            Binding = new Binding(nameof(ImportResultRowViewModel.Id)),
            Width = new DataGridLength(220),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("DataImport_ResultError"),
            Binding = new Binding(nameof(ImportResultRowViewModel.Error)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
    }
}
