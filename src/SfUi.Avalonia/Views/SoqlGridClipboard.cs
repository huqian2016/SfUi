using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>
/// Avalonia の DataGrid はテンプレート列のセル コピー（Ctrl+C）が空になるため、
/// 選択中の行（未選択なら全行）を TSV 形式でクリップボードへ書き出す。
/// </summary>
internal static class SoqlGridClipboard
{
    /// <summary>Ctrl+C（macOS は ⌘+C）をフックして TSV をコピーする。</summary>
    public static void Attach(DataGrid grid, Func<IReadOnlyList<SoqlGridColumn>> columns, Func<IReadOnlyList<SoqlGridRow>> visibleRows)
    {
        grid.AddHandler(InputElement.KeyDownEvent, async (_, e) =>
        {
            if (e.Key != Key.C || !(e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                return;
            }

            var text = BuildText(grid, columns(), visibleRows());
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            e.Handled = true;
            if (TopLevel.GetTopLevel(grid)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>選択中の行（複数選択可能な設定のとき）または全表示行を TSV にする。</summary>
    public static string BuildText(DataGrid grid, IReadOnlyList<SoqlGridColumn> columns, IReadOnlyList<SoqlGridRow> visibleRows)
    {
        if (columns.Count == 0)
        {
            return string.Empty;
        }

        var rows = grid.SelectedItems is { Count: > 0 } selected
            ? selected.OfType<SoqlGridRow>().ToList()
            : visibleRows.ToList();
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        builder.AppendLine(string.Join('\t', columns.Select(column => column.Key)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join('\t', columns.Select((_, index) =>
                index < row.Cells.Count ? row.Cells[index].Value?.Replace("\t", " ") ?? string.Empty : string.Empty)));
        }

        return builder.ToString();
    }
}
