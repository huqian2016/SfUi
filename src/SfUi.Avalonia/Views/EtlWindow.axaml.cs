using Avalonia.Controls;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Views;

/// <summary>ETL 移行ウィンドウ（4 領域: ジョブ エディタ / 実行モニター / 復元マネージャー / 接続マネージャー）。</summary>
public partial class EtlWindow : Window
{
    public EtlWindow(EtlViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
