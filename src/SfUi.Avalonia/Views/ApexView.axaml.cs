using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

public partial class ApexView : UserControl
{
    private ApexViewModel? _subscribed;
    private readonly DispatcherTimer _suggestionsTimer;

    private ApexViewModel? ViewModel => DataContext as ApexViewModel;

    public ApexView()
    {
        AvaloniaXamlLoader.Load(this);

        var editor = this.FindControl<TextEditor>("ApexEditor")!;
        editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");

        // エディタ → ViewModel（実行時に最新のコードを使うため常時同期）
        editor.TextChanged += (_, _) =>
        {
            if (ViewModel is { } viewModel && viewModel.ApexCode != editor.Text)
            {
                viewModel.ApexCode = editor.Text;
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
        editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (ViewModel is { } caretOwner)
            {
                caretOwner.CaretOffset = editor.CaretOffset;
            }

            ScheduleSuggestions();
        };
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

    private void Subscribe()
    {
        var editor = this.FindControl<TextEditor>("ApexEditor")!;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribed = ViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
            editor.Text = _subscribed.ApexCode;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var editor = this.FindControl<TextEditor>("ApexEditor")!;
        if (e.PropertyName == nameof(ApexViewModel.ApexCode)
            && sender is ApexViewModel viewModel
            && editor.Text != viewModel.ApexCode)
        {
            editor.Text = viewModel.ApexCode;
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

        var editor = this.FindControl<TextEditor>("ApexEditor")!;
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

        var editor = this.FindControl<TextEditor>("ApexEditor")!;
        var caret = editor.CaretOffset;
        var context = ApexCompletionParser.Parse(editor.Text, caret);
        var segmentStart = caret - context.Prefix.Length;

        editor.Document.Replace(segmentStart, caret - segmentStart, item.Text);
        editor.CaretOffset = segmentStart + item.Text.Length + item.CaretOffsetDelta;
        editor.Focus();
        ScheduleSuggestions();
    }
}
