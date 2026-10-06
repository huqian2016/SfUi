using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Highlighting;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

public partial class ApexView : UserControl
{
    private ApexViewModel? _subscribed;
    private readonly DispatcherTimer _suggestionsTimer;

    private ApexViewModel? ViewModel => DataContext as ApexViewModel;

    public ApexView()
    {
        InitializeComponent();

        ApexEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");

        ApexEditor.TextChanged += (_, _) =>
        {
            if (ViewModel is { } viewModel && viewModel.ApexCode != ApexEditor.Text)
            {
                viewModel.ApexCode = ApexEditor.Text;
            }
        };

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
            }
        };

        ApexEditor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _ = ViewModel?.ExecuteCommand.ExecuteAsync(null);
                e.Handled = true;
            }
        };

        // 入力・カーソル移動のたびに候補エリアを更新（少し待ってからまとめて）
        ApexEditor.TextArea.TextEntered += (_, _) => ScheduleSuggestions();
        ApexEditor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (ViewModel is { } caretOwner)
            {
                caretOwner.CaretOffset = ApexEditor.CaretOffset;
            }

            ScheduleSuggestions();
        };
        ApexEditor.TextChanged += (_, _) => ScheduleSuggestions();

        _suggestionsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _suggestionsTimer.Tick += (_, _) =>
        {
            _suggestionsTimer.Stop();
            _ = RefreshSuggestionsAsync();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApexViewModel.ApexCode)
            && sender is ApexViewModel viewModel
            && ApexEditor.Text != viewModel.ApexCode)
        {
            ApexEditor.Text = viewModel.ApexCode;
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
            await viewModel.UpdateSuggestionsAsync(ApexEditor.Text, ApexEditor.CaretOffset);
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

        var caret = ApexEditor.CaretOffset;
        var context = ApexCompletionParser.Parse(ApexEditor.Text, caret);
        var segmentStart = caret - context.Prefix.Length;

        ApexEditor.Document.Replace(segmentStart, caret - segmentStart, item.Text);
        ApexEditor.CaretOffset = segmentStart + item.Text.Length + item.CaretOffsetDelta;
        ApexEditor.Focus();
        ScheduleSuggestions();
    }
}
