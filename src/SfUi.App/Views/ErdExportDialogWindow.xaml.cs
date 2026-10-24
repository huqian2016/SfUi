using System.Windows;
using SfUi.Presentation;

namespace SfUi.App.Views;

/// <summary>ER 図出力の設定ダイアログ。</summary>
public partial class ErdExportDialogWindow : Window
{
    public ErdExportDialogWindow(ErdExportDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public ErdExportDialogViewModel ViewModel { get; }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        // 未選択・形式なし・出力先なしなどのエラーはダイアログ内に表示する
        if (ViewModel.BuildResult() is null)
        {
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
