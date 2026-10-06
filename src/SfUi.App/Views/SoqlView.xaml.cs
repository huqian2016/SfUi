using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

public partial class SoqlView : UserControl
{
    private SoqlViewModel? _subscribed;
    private readonly DispatcherTimer _suggestionsTimer;

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

        // 入力・カーソル移動のたびに候補エリアを更新（少し待ってからまとめて）
        SoqlEditor.TextArea.TextEntered += (_, _) => ScheduleSuggestions();
        SoqlEditor.TextArea.Caret.PositionChanged += (_, _) => ScheduleSuggestions();
        SoqlEditor.TextChanged += (_, _) => ScheduleSuggestions();

        _suggestionsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _suggestionsTimer.Tick += (_, _) =>
        {
            _suggestionsTimer.Stop();
            _ = RefreshSuggestionsAsync();
        };
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

        try
        {
            await viewModel.UpdateSuggestionsAsync(SoqlEditor.Text, SoqlEditor.CaretOffset);
        }
        catch (Exception)
        {
            // 候補更新の失敗は通常の操作を妨げない
        }
    }

    /// <summary>候補チップのクリックで、カーソル位置の語を候補で置き換える。</summary>
    private void OnSuggestionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SoqlCompletionItem item })
        {
            return;
        }

        var caret = SoqlEditor.CaretOffset;
        var context = SoqlCompletionParser.Parse(SoqlEditor.Text, caret);
        var segmentStart = caret - context.Prefix.Length;

        SoqlEditor.Document.Replace(segmentStart, caret - segmentStart, item.Text);
        SoqlEditor.CaretOffset = segmentStart + item.Text.Length + item.CaretOffsetDelta;
        SoqlEditor.Focus();
        ScheduleSuggestions();
    }
}
