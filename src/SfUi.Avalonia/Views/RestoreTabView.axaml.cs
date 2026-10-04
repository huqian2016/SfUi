using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>復元タブ（バックアップ選択 → オブジェクト選択 → 復元実行）。</summary>
public partial class RestoreTabView : UserControl
{
    private bool _languageHooked;

    public RestoreTabView()
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
        BuildObjectColumns();
        BuildResultColumns();
    }

    private void BuildObjectColumns()
    {
        var grid = this.FindControl<DataGrid>("ObjectsGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = string.Empty,
            Width = new DataGridLength(36),
            CellTemplate = new FuncDataTemplate<RestoreObjectRowViewModel>((_, _) =>
            {
                var check = new CheckBox();
                check.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(RestoreObjectRowViewModel.IsSelected)) { Mode = BindingMode.TwoWay });
                return check;
            }),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("Backup_ColObject"),
            Binding = new Binding(nameof(RestoreObjectRowViewModel.Name)),
            Width = new DataGridLength(200),
            IsReadOnly = true,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("Backup_ColLabel"),
            Binding = new Binding(nameof(RestoreObjectRowViewModel.Label)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            IsReadOnly = true,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiText.T("Backup_ColCount"),
            Binding = new Binding(nameof(RestoreObjectRowViewModel.CountText)),
            Width = new DataGridLength(100),
            IsReadOnly = true,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Engine",
            Binding = new Binding(nameof(RestoreObjectRowViewModel.EngineText)),
            Width = new DataGridLength(70),
            IsReadOnly = true,
        });
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiText.T("Restore_KeyLabel"),
            Width = new DataGridLength(210),
            CellTemplate = new FuncDataTemplate<RestoreObjectRowViewModel>((_, _) =>
            {
                var combo = new ComboBox { MinWidth = 120, Margin = new Thickness(2, 0) };
                combo.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(RestoreObjectRowViewModel.KeyFields)));
                combo.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(RestoreObjectRowViewModel.KeyField)) { Mode = BindingMode.TwoWay });
                return combo;
            }),
        });
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiText.T("Restore_Details"),
            Width = new DataGridLength(90),
            CellTemplate = new FuncDataTemplate<RestoreObjectRowViewModel>((_, _) => BuildDetailsButton()),
        });
    }

    private void BuildResultColumns()
    {
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(Text(UiText.T("Restore_ColObject"), nameof(RestoreObjectResult.ObjectName), 200));
        grid.Columns.Add(Text(UiText.T("Restore_ColCreated"), nameof(RestoreObjectResult.Created), 90));
        grid.Columns.Add(Text(UiText.T("Restore_ColUpdated"), nameof(RestoreObjectResult.Updated), 90));
        grid.Columns.Add(Text(UiText.T("Restore_ColUndeleted"), nameof(RestoreObjectResult.Undeleted), 90));
        grid.Columns.Add(Text(UiText.T("Restore_ColSkipped"), nameof(RestoreObjectResult.Skipped), 90));
        grid.Columns.Add(Text(UiText.T("Restore_ColFailed"), nameof(RestoreObjectResult.Failed), 90));
        grid.Columns.Add(Text(UiText.T("Restore_ColError"), nameof(RestoreObjectResult.ErrorMessage), 300));
    }

    /// <summary>レコード詳細を開く小さなボタン。</summary>
    private Button BuildDetailsButton()
    {
        var button = new Button
        {
            Content = "↗",
            Padding = new Thickness(6, 0),
            Margin = new Thickness(2, 0),
            FontSize = 11,
        };
        button.Click += (sender, _) =>
        {
            if (DataContext is RestoreTabViewModel viewModel && (sender as Control)?.DataContext is RestoreObjectRowViewModel row)
            {
                viewModel.OpenRecordsCommand.Execute(row);
            }
        };
        return button;
    }

    private static DataGridTextColumn Text(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(width),
    };
}
