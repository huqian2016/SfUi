using System.ComponentModel;
using System.IO;
using System.Xml;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using SfUi.App.ViewModels;
using SfUi.Avalonia.Converters;
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
            _subscribed.ResultColumnsChanged -= RebuildResults;
        }

        _subscribed = ViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
            _subscribed.ResultColumnsChanged += RebuildResults;
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
        var suffix = SoqlSuggestionInsertion.SeparatorFor(editor.Text, caret, context, item);

        editor.Document.Replace(segmentStart, caret - segmentStart, item.Text + suffix);
        editor.CaretOffset = SoqlSuggestionInsertion.CaretAfterInsert(editor.Text, segmentStart, item, suffix);
        editor.Focus();
        ScheduleSuggestions();
    }

    /// <summary>結果グリッドの列を作り直す（Id = リンク・システム項目 = 読み取り専用・それ以外 = 編集可）。</summary>
    private void RebuildResults()
    {
        var grid = this.FindControl<DataGrid>("SoqlGrid")!;
        grid.Columns.Clear();

        if (ViewModel is not { } viewModel)
        {
            return;
        }

        for (var i = 0; i < viewModel.Columns.Count; i++)
        {
            var index = i;
            var column = viewModel.Columns[index];
            if (column.IsId)
            {
                grid.Columns.Add(new DataGridTemplateColumn
                {
                    Header = column.Key,
                    Width = new DataGridLength(170),
                    CellTemplate = new FuncDataTemplate<SoqlGridRow>((_, _) => BuildIdCell()),
                });
                continue;
            }

            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = column.Key,
                Width = new DataGridLength(160),
                CellTemplate = column.IsReadOnly
                    ? new FuncDataTemplate<SoqlGridRow>((_, _) => BuildReadOnlyCell(index))
                    : new FuncDataTemplate<SoqlGridRow>((_, _) => BuildEditCell(index)),
            });
        }
    }

    /// <summary>読み取り専用セル（テキスト表示 + 反映エラーのツールチップ）。</summary>
    private static Control BuildReadOnlyCell(int index)
    {
        var text = new TextBlock
        {
            Margin = new Thickness(4, 2),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        text.Bind(TextBlock.TextProperty, new Binding($"Cells[{index}].Value"));
        text.Bind(ToolTip.TipProperty, new Binding($"Cells[{index}].Error"));
        return text;
    }

    /// <summary>編集可能セル（未反映 = 黄 / 反映エラー = 赤の背景）。</summary>
    private static Control BuildEditCell(int index)
    {
        // 背景は外側の Border で描く（TextBox のテーマ背景がフォーカス時に白で上書きするため）
        var border = new Border
        {
            Padding = new Thickness(0),
        };
        border.Bind(Border.BackgroundProperty, new Binding($"Cells[{index}].StyleState")
        {
            Converter = new SoqlCellBrushConverter(),
        });

        var box = new TextBox
        {
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
        };
        box.Classes.Add("soql-edit-cell");
        box.Bind(TextBox.TextProperty, new Binding($"Cells[{index}].Value") { Mode = BindingMode.TwoWay });
        box.Bind(ToolTip.TipProperty, new Binding($"Cells[{index}].Error"));

        border.Child = box;
        return border;
    }

    /// <summary>Id セル（クリックでレコードページを開くリンク）。</summary>
    private Control BuildIdCell()
    {
        var openRecord = UiText.T("Soql_OpenRecord");
        var button = new Button
        {
            Padding = new Thickness(4, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#1B6ACB")),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.Bind(ContentControl.ContentProperty, new Binding(nameof(SoqlGridRow.RecordId)));
        ToolTip.SetTip(button, openRecord);
        AutomationProperties.SetName(button, openRecord);
        button.Click += (sender, _) =>
        {
            if ((sender as Control)?.DataContext is SoqlGridRow row)
            {
                ViewModel?.OpenRecordCommand.Execute(row);
            }
        };
        return button;
    }
}
