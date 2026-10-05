using System.ComponentModel;
using System.Data;
using System.IO;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

public partial class SoqlView : UserControl
{
    private SoqlViewModel? _subscribed;
    private CompletionWindow? _completionWindow;
    private int _completionSegmentStart = -1;

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

        // 入力しながら候補を表示・更新
        editor.TextArea.TextEntered += (_, e) => _ = OnTextEnteredAsync(e.Text ?? string.Empty);

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

    // ---- 入力補完 ----

    private static bool IsCompletionInput(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == ',' || c == ' ';

    private async Task OnTextEnteredAsync(string text)
    {
        if (text.Length == 0 || !IsCompletionInput(text[0]))
        {
            CloseCompletion();
            return;
        }

        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var editor = this.FindControl<TextEditor>("SoqlEditor")!;
        var source = editor.Text;
        var caret = editor.CaretOffset;
        var context = SoqlCompletionParser.Parse(source, caret);
        var segmentStart = caret - context.Prefix.Length;

        // 表示中の候補はライブラリのフィルタリングに任せる
        if (_completionWindow is not null && segmentStart == _completionSegmentStart)
        {
            return;
        }

        CloseCompletion();

        SoqlCompletionResult? result;
        try
        {
            result = await viewModel.QueryCompletionAsync(source, caret);
        }
        catch (Exception)
        {
            return;
        }

        if (result is null || result.Items.Count == 0)
        {
            return;
        }

        if (editor.Text != source || editor.CaretOffset != caret)
        {
            return;   // 候補取得中に入力が変わった
        }

        ShowCompletion(result, caret);
    }

    private void ShowCompletion(SoqlCompletionResult result, int caret)
    {
        var segmentStart = caret - result.Context.Prefix.Length;
        var editor = this.FindControl<TextEditor>("SoqlEditor")!;
        var window = new CompletionWindow(editor.TextArea)
        {
            CloseWhenCaretAtBeginning = false,
            Width = 400,
            MaxHeight = 300,
        };
        window.StartOffset = segmentStart;
        window.EndOffset = caret;

        foreach (var item in result.Items)
        {
            window.CompletionList.CompletionData.Add(new SoqlCompletionData(item));
        }

        // クリック（ポインター押下）で自動補完
        window.CompletionList.CompletionAcceptAction = CompletionAcceptAction.PointerPressed;

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_completionWindow, window))
            {
                _completionWindow = null;
            }
        };

        window.Show();
        try
        {
            window.CompletionList.SelectItem(result.Context.Prefix);
        }
        catch (Exception)
        {
            // テンプレート適用前に呼ばれた場合は無視（次の文字入力でフィルタされる）
        }

        _completionWindow = window;
        _completionSegmentStart = segmentStart;
    }

    private void CloseCompletion()
    {
        var window = _completionWindow;
        _completionWindow = null;
        _completionSegmentStart = -1;
        window?.Hide();
    }

    /// <summary>補完候補 1 件（挿入時にカーソル位置も調整する）。</summary>
    private sealed class SoqlCompletionData : ICompletionData
    {
        private readonly SoqlCompletionItem _item;

        public SoqlCompletionData(SoqlCompletionItem item) => _item = item;

        public IImage Image => null!;

        public string Text => _item.Text;

        public object Content => _item.Text;

        public object Description => string.IsNullOrEmpty(_item.Description)
            ? _item.Text
            : $"{_item.Text} — {_item.Description}";

        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, _item.Text);
            textArea.Caret.Offset = completionSegment.Offset + _item.Text.Length + _item.CaretOffsetDelta;
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
