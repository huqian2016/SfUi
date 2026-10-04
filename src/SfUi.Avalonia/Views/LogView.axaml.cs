using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        AvaloniaXamlLoader.Load(this);
        BuildColumns();

        // 行のダブルクリックでログ本文を取得
        this.FindControl<DataGrid>("LogGrid")!.DoubleTapped += (_, e) =>
        {
            if (DataContext is LogViewModel viewModel)
            {
                viewModel.FetchCommand.Execute(null);
                e.Handled = true;
            }
        };

        UiText.LanguageChanged += BuildColumns;
        DetachedFromVisualTree += (_, _) => UiText.LanguageChanged -= BuildColumns;
    }

    private void BuildColumns()
    {
        var grid = this.FindControl<DataGrid>("LogGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(Text(UiText.T("Log_ColStart"), "StartTimeLabel", 170));
        grid.Columns.Add(Text(UiText.T("Log_ColOperation"), "Operation", 120));
        grid.Columns.Add(Text(UiText.T("Log_ColStatus"), "Status", 70));
        grid.Columns.Add(Text(UiText.T("Log_ColDuration"), "DurationMs", 70));
        grid.Columns.Add(Text(UiText.T("Log_ColSize"), "LogLength", 70));
    }

    private static DataGridTextColumn Text(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(width),
    };
}
