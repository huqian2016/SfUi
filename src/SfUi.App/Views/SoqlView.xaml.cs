using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

public partial class SoqlView : UserControl
{
    private SoqlViewModel? _subscribed;
    private CompletionWindow? _completionWindow;
    private int _completionSegmentStart = -1;

    private SoqlViewModel? ViewModel => DataContext as SoqlViewModel;

    public SoqlView()
    {
        InitializeComponent();

        // SOQL 専用の構文ハイライト（キーワード = 青 / 関数 = 有色）
        SoqlEditor.SyntaxHighlighting = HighlightingLoader.Load(
            XmlReader.Create(new StringReader(SoqlHighlighting.Xshd)),
            HighlightingManager.Instance);

        // エディタ → ViewModel（すべての操作で最新のテキストを使うため常時同期）
        SoqlEditor.TextChanged += (_, _) =>
        {
            if (ViewModel is { } viewModel && viewModel.SoqlText != SoqlEditor.Text)
            {
                viewModel.SoqlText = SoqlEditor.Text;
            }
        };

        // ViewModel → エディタ（履歴・お気に入りからの読み込み）
        DataContextChanged += (_, _) =>
        {
            if (_subscribed is not null)
            {
                _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _subscribed = ViewModel;
            if (_subscribed is not null)
            {
                _subscribed.PropertyChanged += OnViewModelPropertyChanged;
                SoqlEditor.Text = _subscribed.SoqlText;
            }
        };

        // Ctrl+Enter で実行
        SoqlEditor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _ = ViewModel?.ExecuteCommand.ExecuteAsync(null);
                e.Handled = true;
            }
        };

        // 入力しながら候補を表示・更新
        SoqlEditor.TextArea.TextEntered += (_, e) => _ = OnTextEnteredAsync(e.Text ?? string.Empty);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SoqlViewModel.SoqlText)
            && sender is SoqlViewModel viewModel
            && SoqlEditor.Text != viewModel.SoqlText)
        {
            SoqlEditor.Text = viewModel.SoqlText;
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

        var source = SoqlEditor.Text;
        var caret = SoqlEditor.CaretOffset;
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

        if (SoqlEditor.Text != source || SoqlEditor.CaretOffset != caret)
        {
            return;   // 候補取得中に入力が変わった
        }

        ShowCompletion(result, caret);
    }

    private void ShowCompletion(SoqlCompletionResult result, int caret)
    {
        var segmentStart = caret - result.Context.Prefix.Length;
        var window = new CompletionWindow(SoqlEditor.TextArea)
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

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_completionWindow, window))
            {
                _completionWindow = null;
            }
        };

        // クリックで自動補完（WPF 版 AvalonEdit の既定はダブルクリックのため単クリックにも対応）
        if (window.CompletionList.ListBox is { } listBox)
        {
            listBox.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source
                    && ItemsControl.ContainerFromElement(listBox, source) is ListBoxItem)
                {
                    window.CompletionList.RequestInsertion(e);
                }
            };
        }

        window.Show();
        window.CompletionList.SelectItem(result.Context.Prefix);

        _completionWindow = window;
        _completionSegmentStart = segmentStart;
    }

    private void CloseCompletion()
    {
        var window = _completionWindow;
        _completionWindow = null;
        _completionSegmentStart = -1;
        window?.Close();
    }

    /// <summary>補完候補 1 件（挿入時にカーソル位置も調整する）。</summary>
    private sealed class SoqlCompletionData : ICompletionData
    {
        private readonly SoqlCompletionItem _item;

        public SoqlCompletionData(SoqlCompletionItem item) => _item = item;

        public ImageSource Image => null!;

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
}
