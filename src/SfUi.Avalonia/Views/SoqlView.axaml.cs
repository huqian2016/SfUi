using System.ComponentModel;
using System.Data;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

public partial class SoqlView : UserControl
{
    private SoqlViewModel? _subscribed;

    private SoqlViewModel? ViewModel => DataContext as SoqlViewModel;

    public SoqlView()
    {
        AvaloniaXamlLoader.Load(this);

        var editor = this.FindControl<TextEditor>("SoqlEditor")!;
        editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("SQL");

        // エディタ → ViewModel（すべての操作で最新のテキストを使うため常時同期）
        editor.TextChanged += (_, _) =>
        {
            if (ViewModel is { } viewModel && viewModel.SoqlText != editor.Text)
            {
                viewModel.SoqlText = editor.Text;
            }
        };

        // Ctrl+Enter で実行
        editor.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                ViewModel?.ExecuteCommand.Execute(null);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) => Subscribe();
        Subscribe();
    }

    /// <summary>ViewModel → エディタ（履歴・お気に入りからの読み込み）と結果グリッドの再構築。</summary>
    private void Subscribe()
    {
        var editor = this.FindControl<TextEditor>("SoqlEditor")!;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribed = ViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
            editor.Text = _subscribed.SoqlText;
        }

        RebuildResults();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var editor = this.FindControl<TextEditor>("SoqlEditor")!;
        if (e.PropertyName == nameof(SoqlViewModel.SoqlText)
            && sender is SoqlViewModel viewModel
            && editor.Text != viewModel.SoqlText)
        {
            editor.Text = viewModel.SoqlText;
        }

        if (e.PropertyName == nameof(SoqlViewModel.ResultView))
        {
            RebuildResults();
        }
    }

    /// <summary>DataView の結果を Avalonia DataGrid の列（数値インデクサー バインド）へ変換する。</summary>
    private void RebuildResults()
    {
        var grid = this.FindControl<DataGrid>("SoqlGrid")!;
        grid.Columns.Clear();
        grid.ItemsSource = null;

        if (ViewModel?.ResultView is not { } view || view.Table is null)
        {
            return;
        }

        var columns = view.Table.Columns;
        var rows = new List<object?[]>(view.Count);
        foreach (DataRowView row in view)
        {
            var values = new object?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                values[i] = row.Row[i] is DBNull ? null : row.Row[i];
            }

            rows.Add(values);
        }

        for (var i = 0; i < columns.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i].ColumnName,
                Binding = new Binding($"[{i}]"),
                Width = new DataGridLength(140),
            });
        }

        grid.ItemsSource = rows;
    }
}
