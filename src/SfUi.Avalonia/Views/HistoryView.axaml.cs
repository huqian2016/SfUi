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

        var historyGrid = this.FindControl<DataGrid>("HistoryGrid")!;

        // 行のダブルクリックで履歴を再実行
        historyGrid.DoubleTapped += (_, e) =>
        {
            if (DataContext is HistoryViewModel viewModel && viewModel.SelectedEntry is { } entry)
            {
                viewModel.RequestReplay(entry);
                e.Handled = true;
            }
        };

        // 複数選択を ViewModel へ反映する（一括削除の対象）
        historyGrid.SelectionChanged += (_, _) =>
        {
            var rows = new List<HistoryEntry>();
            if (historyGrid.SelectedItems is { } selected)
            {
                foreach (var item in selected)
                {
                    if (item is HistoryEntry entry)
                    {
                        rows.Add(entry);
                    }
                }
            }

            if (DataContext is HistoryViewModel viewModel)
            {
                viewModel.SetSelectedEntries(rows);
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
