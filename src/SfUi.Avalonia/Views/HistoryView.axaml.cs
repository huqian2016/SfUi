using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        AvaloniaXamlLoader.Load(this);
        BuildColumns();

        // 行のダブルクリックで履歴を再実行
        this.FindControl<DataGrid>("HistoryGrid")!.DoubleTapped += (_, e) =>
        {
            if (DataContext is HistoryViewModel viewModel && viewModel.SelectedEntry is { } entry)
            {
                viewModel.RequestReplay(entry);
                e.Handled = true;
            }
        };

        UiText.LanguageChanged += BuildColumns;
        DetachedFromVisualTree += (_, _) => UiText.LanguageChanged -= BuildColumns;
    }

    private void BuildColumns()
    {
        var grid = this.FindControl<DataGrid>("HistoryGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        grid.Columns.Add(Text(UiText.T("History_ColTime"), "TimestampLocal", 140));
        grid.Columns.Add(Text(UiText.T("History_ColType"), "TypeLabel", 80));
        grid.Columns.Add(Text(UiText.T("History_ColOrg"), "Org", 110));
        grid.Columns.Add(Text(UiText.T("History_ColFolder"), "Folder", 200));
        grid.Columns.Add(Text(UiText.T("History_ColSummary"), "Summary", 360));
        grid.Columns.Add(Text(UiText.T("History_ColStatus"), "StatusLabel", 60));
        grid.Columns.Add(Text(UiText.T("History_ColDuration"), "DurationMs", 70));
    }

    private static DataGridTextColumn Text(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(width),
    };
}
