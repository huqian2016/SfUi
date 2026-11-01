using Avalonia.Controls;
using Avalonia.Interactivity;
using SfUi.App.ViewModels;
using SfUi.Presentation;

namespace SfUi.Avalonia.Dialogs;

/// <summary>接続アプリの作成 / 編集ダイアログ（Avalonia）。</summary>
public partial class ConnectedAppEditorDialog : Window
{
    public ConnectedAppEditorDialog()
    {
        InitializeComponent();
    }

    public ConnectedAppEditorResult? Result { get; private set; }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectedAppEditorViewModel viewModel)
        {
            return;
        }

        var result = viewModel.BuildResult();
        if (result is null)
        {
            return;
        }

        Result = result;
        Close(result);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
