using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

public partial class ApexView : UserControl
{
    private ApexViewModel? _subscribed;

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
}
