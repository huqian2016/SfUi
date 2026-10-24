using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>レポート出力の設定ダイアログ。</summary>
public partial class ReportExportDialogWindow : Window
{
    public ReportExportDialogWindow(ReportExportDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public ReportExportDialogViewModel ViewModel { get; }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        // 形式未選択・出力先未入力などのエラーはダイアログ内に表示する
        if (ViewModel.BuildOptions() is null)
        {
            return;
        }

        DialogResult = true;
    }
}
