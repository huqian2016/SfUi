using Avalonia.Controls;
using Avalonia.Interactivity;
using SfUi.Presentation;

namespace SfUi.Avalonia.Dialogs;

/// <summary>ER 図出力の設定ダイアログ（Avalonia）。</summary>
public partial class ErdExportDialog : Window
{
    public ErdExportDialog() => InitializeComponent();

    public ErdExportDialog(ErdExportDialogViewModel viewModel)
        : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public ErdExportDialogViewModel ViewModel { get; } = null!;

    private void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.BuildResult() is null)
        {
            return;
        }

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
