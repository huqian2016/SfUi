using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

public partial class SoqlView : UserControl
{
    private SoqlViewModel? _subscribed;

    private SoqlViewModel? ViewModel => DataContext as SoqlViewModel;

    public SoqlView()
    {
        InitializeComponent();

        SoqlEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("SQL");

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
}
