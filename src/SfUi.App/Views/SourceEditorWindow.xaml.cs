using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>
/// ソース エディタ ウィンドウ（Phase 2: 編集 + 未反映ハイライト + 組織反映）。
/// 左 = 種類別メンバー ツリー、右 = ファイル タブ（行番号 + シンタックス ハイライト + 未反映行の背景色）。
/// </summary>
public partial class SourceEditorWindow : Window
{
    private readonly Dictionary<SourceFileViewModel, TextEditor> _editors = new();
    private readonly DispatcherTimer _suggestTimer;
    private SourceNavigation? _pendingNav;
    private TextEditor? _suggestEditor;

    public SourceEditorWindow(SourceEditorViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Ctrl+S = 組織へ反映（InputBindings は DataContext を継承しないためコードで設定）
        InputBindings.Add(new KeyBinding(viewModel.DeployCommand, Key.S, ModifierKeys.Control));

        // 入力・カーソル移動のたびに補完候補を更新（少し待ってからまとめて）
        _suggestTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _suggestTimer.Tick += (_, _) =>
        {
            _suggestTimer.Stop();
            _ = RefreshSuggestionsAsync();
        };
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

    /// <summary>保留中の行ジャンプを実行する（対象タブのエディタが未生成なら、生成時の Loaded で再試行）。</summary>
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

    private void SourceEditorTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SourceMemberInfo member)
        {
            ViewModel.OpenMemberCommand.Execute(member);
        }
    }

    private void NewMember_Click(object sender, RoutedEventArgs e)
    {
        var result = NewMemberDialog.Show(this, ViewModel.GetExistingNames);
        if (result is { } choice)
        {
            _ = ViewModel.CreateMemberAsync(choice.Kind, choice.Name, choice.SObject);
        }
    }

    private void Editor_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextEditor editor)
        {
            return;
        }

        BindEditor(editor);
        editor.TextArea.TextView.Redraw();
        TryNavigate();
    }

    /// <summary>
    /// エディタとファイル VM を結線する（Loaded と DataContext 変更の両方から呼ばれる）。
    /// TabControl が同じエディタ インスタンスを別ファイルへ再利用することがあるため、
    /// 古い結線を外してから本文を入れ直し、マップを登録し直す。
    /// </summary>
    private void BindEditor(TextEditor editor)
    {
        ApplyHighlighting(editor);

        editor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
        editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        editor.TextArea.TextEntered -= Editor_TextEntered;
        editor.TextArea.TextEntered += Editor_TextEntered;
        editor.DataContextChanged -= Editor_DataContextChanged;
        editor.DataContextChanged += Editor_DataContextChanged;
        editor.TextChanged -= Editor_TextChanged;
        editor.TextChanged += Editor_TextChanged;

        // AvalonEdit の Text は DependencyProperty ではないため XAML バインド不可（クラッシュする）。
        // 表示時にコードビハインドで設定し、編集は TextChanged で VM へ同期する。
        if (editor.DataContext is not SourceFileViewModel file)
        {
            return;
        }

        foreach (var stale in _editors.Where(kv => ReferenceEquals(kv.Value, editor)).Select(kv => kv.Key).ToList())
        {
            _editors.Remove(stale);
            stale.PropertyChanged -= File_PropertyChanged;
        }

        if (!string.Equals(editor.Text, file.Text, StringComparison.Ordinal))
        {
            editor.Text = file.Text;
        }

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

    private void Editor_DataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextEditor editor)
        {
            BindEditor(editor);
        }
    }

    private void Editor_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextEditor editor)
        {
            return;
        }

        editor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
        editor.TextArea.TextEntered -= Editor_TextEntered;
        editor.DataContextChanged -= Editor_DataContextChanged;
        editor.TextChanged -= Editor_TextChanged;
        if (editor.DataContext is SourceFileViewModel file)
        {
            file.PropertyChanged -= File_PropertyChanged;
            _editors.Remove(file);
        }

        if (ReferenceEquals(_suggestEditor, editor))
        {
            _suggestEditor = null;
        }

        if (editor.Tag is SourceLineColorizer colorizer)
        {
            editor.TextArea.TextView.LineTransformers.Remove(colorizer);
            editor.Tag = null;
        }
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (sender is not TextEditor editor)
        {
            return;
        }

        if (editor.DataContext is SourceFileViewModel file
            && !string.Equals(file.Text, editor.Text, StringComparison.Ordinal))
        {
            file.Text = editor.Text;
        }

        _suggestEditor = editor;
        ScheduleSuggestions();
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
        if (sender is not ICSharpCode.AvalonEdit.Editing.Caret caret)
        {
            return;
        }

        ViewModel.CaretText = $"Ln {caret.Line}, Col {caret.Column}";
        var editor = _editors.Values.FirstOrDefault(ed => ReferenceEquals(ed.TextArea.Caret, caret));
        if (editor is not null)
        {
            _suggestEditor = editor;
            ScheduleSuggestions();
        }
    }

    // ---- 自動補完（候補チップ。Phase 5）----

    private void Editor_TextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (sender is not ICSharpCode.AvalonEdit.Editing.TextArea textArea)
        {
            return;
        }

        var editor = _editors.Values.FirstOrDefault(ed => ReferenceEquals(ed.TextArea, textArea));
        if (editor is not null)
        {
            _suggestEditor = editor;
            ScheduleSuggestions();
        }
    }

    private void ScheduleSuggestions()
    {
        _suggestTimer.Stop();
        _suggestTimer.Start();
    }

    private async Task RefreshSuggestionsAsync()
    {
        if (_suggestEditor is not { } editor || editor.DataContext is not SourceFileViewModel file)
        {
            return;
        }

        try
        {
            await ViewModel.UpdateSuggestionsAsync(file, editor.CaretOffset);
        }
        catch (Exception)
        {
            // 候補更新の失敗は通常の操作を妨げない
        }
    }

    /// <summary>候補チップのクリックで、カーソル位置の語を候補で置き換える。</summary>
    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SoqlCompletionItem item })
        {
            return;
        }

        var file = ViewModel.SelectedFile;
        if (file is null || !_editors.TryGetValue(file, out var editor))
        {
            return;
        }

        var caret = editor.CaretOffset;
        var start = ViewModel.GetSuggestionSegmentStart(file, editor.Text, caret);
        if (start < 0 || start > caret)
        {
            return;
        }

        editor.Document.Replace(start, caret - start, item.Text);
        editor.CaretOffset = start + item.Text.Length + item.CaretOffsetDelta;
        editor.Focus();
        _suggestEditor = editor;
        ScheduleSuggestions();
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
