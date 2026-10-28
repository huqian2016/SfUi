using System.Windows;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>レポート ビルダー（レポート定義の編集）ダイアログ。</summary>
public partial class ReportEditorDialog : Window
{
    public ReportEditorDialog(ReportEditorViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public ReportEditorViewModel ViewModel { get; }

    /// <summary>編集結果（キャンセル時は null）。</summary>
    public ReportDefinition? Result { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var result = ViewModel.TryBuild();
        if (result is null)
        {
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
