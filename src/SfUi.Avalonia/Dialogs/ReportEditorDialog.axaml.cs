using Avalonia.Controls;
using Avalonia.Interactivity;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Dialogs;

/// <summary>レポート ビルダー（レポート定義の編集）ダイアログ（Avalonia）。</summary>
public partial class ReportEditorDialog : Window
{
    public ReportEditorDialog()
    {
        InitializeComponent();
    }

    public ReportEditorDialog(ReportEditorViewModel viewModel)
        : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public ReportEditorViewModel ViewModel { get; } = null!;

    /// <summary>編集結果（キャンセル時は null）。</summary>
    public ReportDefinition? Result { get; private set; }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var result = ViewModel.TryBuild();
        if (result is null)
        {
            return;
        }

        Result = result;
        Close(result);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
