using System.ComponentModel;
using System.Data;
using System.IO;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

public partial class SoqlView : UserControl
{
    private SoqlViewModel? _subscribed;
    private readonly DispatcherTimer _suggestionsTimer;

    private SoqlViewModel? ViewModel => DataContext as SoqlViewModel;

    public SoqlView()
    {
        AvaloniaXamlLoader.Load(this);

        var editor = this.FindControl<TextEditor>("SoqlEditor")!;

        // SOQL 専用の構文ハイライト（キーワード = 青 / 関数 = 有色）
        editor.SyntaxHighlighting = HighlightingLoader.Load(
            XmlReader.Create(new StringReader(SoqlHighlighting.Xshd)),
            HighlightingManager.Instance);

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

        // 入力・カーソル移動のたびに候補エリアを更新（少し待ってからまとめて）
        editor.TextArea.TextEntered += (_, _) => ScheduleSuggestions();
        editor.TextArea.Caret.PositionChanged += (_, _) => ScheduleSuggestions();
        editor.TextChanged += (_, _) => ScheduleSuggestions();

        _suggestionsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _suggestionsTimer.Tick += (_, _) =>
        {
            _suggestionsTimer.Stop();
            _ = RefreshSuggestionsAsync();
        };

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

    // ---- 候補エリア ----

    private void ScheduleSuggestions()
    {
        _suggestionsTimer.Stop();
        _suggestionsTimer.Start();
    }

    private async Task RefreshSuggestionsAsync()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var editor = this.FindControl<TextEditor>("SoqlEditor")!;
        try
        {
            await viewModel.UpdateSuggestionsAsync(editor.Text, editor.CaretOffset);
        }
        catch (Exception)
        {
            // 候補更新の失敗は通常の操作を妨げない
        }
    }

    /// <summary>候補チップのクリックで、カーソル位置の語を候補で置き換える。</summary>
    private void OnSuggestionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SoqlCompletionItem item })
        {
            return;
        }

        var editor = this.FindControl<TextEditor>("SoqlEditor")!;
        var caret = editor.CaretOffset;
        var context = SoqlCompletionParser.Parse(editor.Text, caret);
        var segmentStart = caret - context.Prefix.Length;

        editor.Document.Replace(segmentStart, caret - segmentStart, item.Text);
        editor.CaretOffset = segmentStart + item.Text.Length + item.CaretOffsetDelta;
        editor.Focus();
        ScheduleSuggestions();
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
