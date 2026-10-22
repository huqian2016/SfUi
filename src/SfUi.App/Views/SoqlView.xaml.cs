using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

public partial class SoqlView : UserControl
{
    private const string ReadOnlyCellTemplateXaml =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
        + "<TextBlock Text=\"{Binding Cells[__I__].Value}\" Margin=\"4,2\" VerticalAlignment=\"Center\" "
        + "TextTrimming=\"CharacterEllipsis\" ToolTip=\"{Binding Cells[__I__].Error}\" /></DataTemplate>";

    private const string EditableCellTemplateXaml =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
        + "<TextBox Text=\"{Binding Cells[__I__].Value, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\" "
        + "BorderThickness=\"0\" Padding=\"4,2\" Margin=\"0\" VerticalContentAlignment=\"Center\" "
        + "ToolTip=\"{Binding Cells[__I__].Error}\">"
        + "<TextBox.Style><Style TargetType=\"TextBox\">"
        + "<Setter Property=\"Background\" Value=\"Transparent\" />"
        + "<Style.Triggers>"
        + "<DataTrigger Binding=\"{Binding Cells[__I__].IsDirty}\" Value=\"True\"><Setter Property=\"Background\" Value=\"#FFF3CD\" /></DataTrigger>"
        + "<DataTrigger Binding=\"{Binding Cells[__I__].HasError}\" Value=\"True\"><Setter Property=\"Background\" Value=\"#F8D7DA\" /></DataTrigger>"
        + "</Style.Triggers></Style></TextBox.Style>"
        + "</TextBox></DataTemplate>";

    private const string IdCellTemplateXaml =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
        + "<Button Content=\"{Binding RecordId}\" Padding=\"4,2\" Margin=\"2,0\" HorizontalAlignment=\"Left\" "
        + "Background=\"Transparent\" BorderThickness=\"0\" Foreground=\"#1B6ACB\" Cursor=\"Hand\" "
        + "ToolTip=\"__NAME__\" AutomationProperties.Name=\"__NAME__\" "
        + "Command=\"{Binding DataContext.OpenRecordCommand, RelativeSource={RelativeSource AncestorType=UserControl}}\" "
        + "CommandParameter=\"{Binding}\" /></DataTemplate>";

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

        // ViewModel → エディタ（履歴・お気に入りからの読み込み）と結果グリッドの再構築
        DataContextChanged += (_, _) =>
        {
            if (_subscribed is not null)
            {
                _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
                _subscribed.ResultColumnsChanged -= RebuildResultColumns;
            }

            _subscribed = ViewModel;
            if (_subscribed is not null)
            {
                _subscribed.PropertyChanged += OnViewModelPropertyChanged;
                _subscribed.ResultColumnsChanged += RebuildResultColumns;
                SoqlEditor.Text = _subscribed.SoqlText;
            }

            RebuildResultColumns();
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

    /// <summary>結果グリッドの列を作り直す（Id = リンク・システム項目 = 読み取り専用・それ以外 = 編集可）。</summary>
    private void RebuildResultColumns()
    {
        SoqlGrid.Columns.Clear();
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var openRecord = UiText.T("Soql_OpenRecord");
        for (var i = 0; i < viewModel.Columns.Count; i++)
        {
            var index = i;
            var column = viewModel.Columns[index];
            if (column.IsId)
            {
                SoqlGrid.Columns.Add(new DataGridTemplateColumn
                {
                    Header = column.Key,
                    Width = new DataGridLength(170),
                    CellTemplate = (DataTemplate)XamlReader.Parse(IdCellTemplateXaml.Replace("__NAME__", openRecord)),
                });
                continue;
            }

            var template = column.IsReadOnly ? ReadOnlyCellTemplateXaml : EditableCellTemplateXaml;
            SoqlGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = column.Key,
                Width = new DataGridLength(160),
                CellTemplate = (DataTemplate)XamlReader.Parse(template.Replace("__I__", index.ToString())),
            });
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
        var suffix = SoqlSuggestionInsertion.SeparatorFor(SoqlEditor.Text, caret, context, item);

        SoqlEditor.Document.Replace(segmentStart, caret - segmentStart, item.Text + suffix);
        SoqlEditor.CaretOffset = SoqlSuggestionInsertion.CaretAfterInsert(SoqlEditor.Text, segmentStart, item, suffix);
        SoqlEditor.Focus();
        ScheduleSuggestions();
    }
}
