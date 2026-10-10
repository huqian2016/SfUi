using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

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

    private void SourceEditorTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SourceMemberInfo member)
        {
            ViewModel.OpenMemberCommand.Execute(member);
        }
    }

    private void Editor_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextEditor editor)
        {
            return;
        }

        // AvalonEdit の Text は DependencyProperty ではないため XAML バインド不可（クラッシュする）。
        // 表示時にコードビハインドで本文を設定する（Phase 1 は閲覧のみ）。
        if (editor.DataContext is SourceFileViewModel file && editor.Text != file.Text)
        {
            editor.Text = file.Text;
        }

        ApplyHighlighting(editor);
        editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
    }

    private void Editor_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextEditor editor)
        {
            editor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
        }
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        if (sender is ICSharpCode.AvalonEdit.Editing.Caret caret)
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
