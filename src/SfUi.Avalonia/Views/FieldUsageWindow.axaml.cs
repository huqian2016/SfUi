using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>項目の使用箇所（フィールド影響分析）ウィンドウ（非モーダル・複数同時表示可）。</summary>
public partial class FieldUsageWindow : Window
{
    private readonly FieldUsageViewModel _viewModel = null!;

    /// <summary>XAML ローダー用（実際の生成はコンストラクター経由）。</summary>
    public FieldUsageWindow() => AvaloniaXamlLoader.Load(this);

    public FieldUsageWindow(FieldUsageViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        var grid = this.FindControl<DataGrid>("HitsGrid");
        if (grid is not null)
        {
            grid.DoubleTapped += (_, _) =>
            {
                if (_viewModel.SelectedRow is { } row)
                {
                    _viewModel.OpenRowCommand.Execute(row);
                }
            };
        }
    }

    public FieldUsageViewModel ViewModel => _viewModel;
}
