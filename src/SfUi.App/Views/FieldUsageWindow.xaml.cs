using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>項目の使用箇所（フィールド影響分析）ウィンドウ（非モーダル・複数同時表示可）。</summary>
public partial class FieldUsageWindow : Window
{
    public FieldUsageWindow(FieldUsageViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public FieldUsageViewModel ViewModel { get; }

    private void Row_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow && ViewModel.SelectedRow is { } row)
        {
            ViewModel.OpenRowCommand.Execute(row);
            e.Handled = true;
        }
    }
}
