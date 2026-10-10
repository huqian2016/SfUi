using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>
/// ソース エディタ ウィンドウ（Phase 1: メタデータ エクスプローラー + 閲覧エディタ）。
/// 左 = 種類別メンバー ツリー、右 = ファイル タブ（行番号 + シンタックス ハイライト）。
/// </summary>
public partial class SourceEditorWindow : Window
{
    public SourceEditorWindow(SourceEditorViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public SourceEditorViewModel ViewModel { get; }

    private void SourceEditorTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is SourceMemberInfo member)
        {
            ViewModel.OpenMemberCommand.Execute(member);
        }
    }

    private void Editor_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextEditor editor)
        {
            return;
        }

        // AvaloniaEdit の Text は XAML バインド不可のため、ここで本文を設定する（Phase 1 は閲覧のみ）
        if (editor.DataContext is SourceFileViewModel file)
        {
            editor.Text = file.Text;
        }

        ApplyHighlighting(editor);
        editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
    }

    private void Editor_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextEditor editor)
        {
            editor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
        }
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        if (sender is AvaloniaEdit.Editing.Caret caret)
        {
            ViewModel.CaretText = $"Ln {caret.Line}, Col {caret.Column}";
        }
    }

    private static void ApplyHighlighting(TextEditor editor)
    {
        var language = (editor.DataContext as SourceFileViewModel)?.LanguageId;
        if (string.IsNullOrEmpty(language))
        {
            return;
        }

        var definition = HighlightingManager.Instance.GetDefinition(language);
        if (definition is not null)
        {
            editor.SyntaxHighlighting = definition;
        }
    }
}
