using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>バックアップ比較タブ。</summary>
public partial class CompareTabView : UserControl
{
    private bool _languageHooked;

    public CompareTabView()
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
        var grid = this.FindControl<DataGrid>("CompareGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(Text(UiText.T("Backup_ColObject"), nameof(BackupCompareObjectRowViewModel.Name), 180));
        grid.Columns.Add(Text(UiText.T("Backup_ColLabel"), nameof(BackupCompareObjectRowViewModel.Label), 300));
        grid.Columns.Add(Text(UiText.T("BackupCompare_ColCountA"), nameof(BackupCompareObjectRowViewModel.CountAText), 80));
        grid.Columns.Add(Text(UiText.T("BackupCompare_ColCountB"), nameof(BackupCompareObjectRowViewModel.CountBText), 80));
        grid.Columns.Add(Text(UiText.T("BackupCompare_ColAdded"), nameof(BackupCompareObjectRowViewModel.AddedText), 70));
        grid.Columns.Add(Text(UiText.T("BackupCompare_ColRemoved"), nameof(BackupCompareObjectRowViewModel.RemovedText), 70));
        grid.Columns.Add(Text(UiText.T("BackupCompare_ColChanged"), nameof(BackupCompareObjectRowViewModel.ChangedText), 70));
        grid.Columns.Add(Text(UiText.T("BackupCompare_ColStatus"), nameof(BackupCompareObjectRowViewModel.StatusText), 90));
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiText.T("Restore_Details"),
            Width = new DataGridLength(80),
            CellTemplate = new FuncDataTemplate<BackupCompareObjectRowViewModel>((_, _) => BuildDetailsButton()),
        });
    }

    /// <summary>レコード単位の差分を開く小さなボタン。</summary>
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
            if (DataContext is CompareTabViewModel viewModel && (sender as Control)?.DataContext is BackupCompareObjectRowViewModel row)
            {
                viewModel.OpenDetailsCommand.Execute(row);
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
