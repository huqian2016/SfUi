using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>
/// ソース エディタ ウィンドウ（Phase 2: 編集 + 未反映ハイライト + 組織反映）。
/// 左 = 種類別メンバー ツリー、右 = ファイル タブ（行番号 + シンタックス ハイライト + 未反映行の背景色）。
/// </summary>
public partial class SourceEditorWindow : Window
{
    private readonly Dictionary<SourceFileViewModel, TextEditor> _editors = new();
    private SourceNavigation? _pendingNav;

    public SourceEditorWindow(SourceEditorViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Ctrl+S = 組織へ反映
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse("Ctrl+S"),
            Command = viewModel.DeployCommand,
        });
    }

    public SourceEditorViewModel ViewModel { get; }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourceEditorViewModel.PendingNavigation) && ViewModel.PendingNavigation is { } nav)
        {
            _pendingNav = nav;
            TryNavigate();
        }
    }

    /// <summary>保留中の行ジャンプを実行する（対象タブのエディタが未生成なら、生成時の Attached で再試行）。</summary>
    private void TryNavigate()
    {
        if (_pendingNav is not { } nav)
        {
            return;
        }

        var file = ViewModel.OpenFiles.FirstOrDefault(f => string.Equals(f.FileName, nav.FileName, StringComparison.OrdinalIgnoreCase))
                   ?? ViewModel.SelectedFile;
        if (file is not null && _editors.TryGetValue(file, out var editor))
        {
            var line = Math.Clamp(nav.Line, 1, Math.Max(editor.Document.LineCount, 1));
            editor.ScrollTo(line, 1);
            editor.TextArea.Caret.Line = line;
            editor.TextArea.Caret.Column = 1;
            editor.Focus();
            _pendingNav = null;
            ViewModel.ClearPendingNavigation();
        }
    }

    private void SourceEditorTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is SourceMemberInfo member)
        {
            ViewModel.OpenMemberCommand.Execute(member);
        }
    }

    private void NewMember_Click(object? sender, RoutedEventArgs e)
    {
        var result = NewMemberDialog.Show(this, ViewModel.GetExistingNames);
        if (result is { } choice)
        {
            _ = ViewModel.CreateMemberAsync(choice.Kind, choice.Name, choice.SObject);
        }
    }

    private void Editor_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextEditor editor)
        {
            return;
        }

        ApplyHighlighting(editor);

        // AvaloniaEdit の Text は XAML バインド不可のため、ここで本文を設定し TextChanged で VM へ同期する
        if (editor.DataContext is SourceFileViewModel file)
        {
            if (!string.Equals(editor.Text, file.Text, StringComparison.Ordinal))
            {
                editor.Text = file.Text;
            }

            editor.TextChanged -= Editor_TextChanged;
            editor.TextChanged += Editor_TextChanged;
            file.PropertyChanged -= File_PropertyChanged;
            file.PropertyChanged += File_PropertyChanged;
            _editors[file] = editor;

            if (editor.Tag is not SourceLineColorizer colorizer)
            {
                colorizer = new SourceLineColorizer();
                editor.TextArea.TextView.LineTransformers.Add(colorizer);
                editor.Tag = colorizer;
            }

            colorizer.ChangedLines = file.ChangedLines;
        }

        editor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
        editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        editor.TextArea.TextView.Redraw();
        TryNavigate();
    }

    private void Editor_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextEditor editor)
        {
            return;
        }

        editor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
        editor.TextChanged -= Editor_TextChanged;
        if (editor.DataContext is SourceFileViewModel file)
        {
            file.PropertyChanged -= File_PropertyChanged;
            _editors.Remove(file);
        }

        if (editor.Tag is SourceLineColorizer colorizer)
        {
            editor.TextArea.TextView.LineTransformers.Remove(colorizer);
            editor.Tag = null;
        }
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (sender is TextEditor editor
            && editor.DataContext is SourceFileViewModel file
            && !string.Equals(file.Text, editor.Text, StringComparison.Ordinal))
        {
            file.Text = editor.Text;
        }
    }

    private void File_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourceFileViewModel.ChangedLines)
            && sender is SourceFileViewModel file
            && _editors.TryGetValue(file, out var editor)
            && editor.Tag is SourceLineColorizer colorizer)
        {
            colorizer.ChangedLines = file.ChangedLines;
            editor.TextArea.TextView.Redraw();
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
