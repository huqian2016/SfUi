using Avalonia.Controls;
using Avalonia.Interactivity;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Dialogs;

/// <summary>レポート出力の設定ダイアログ（Avalonia）。</summary>
public partial class ReportExportDialog : Window
{
    public ReportExportDialog() => InitializeComponent();

    public ReportExportDialog(ReportExportDialogViewModel viewModel)
        : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public ReportExportDialogViewModel ViewModel { get; } = null!;

    private void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.BuildOptions() is null)
        {
            return;
        }

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
