using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>ETL 移行ウィンドウ（4 領域: ジョブ エディタ / 実行モニター / 復元マネージャー / 接続マネージャー）。</summary>
public partial class EtlWindow : Window
{
    public EtlWindow(EtlViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public EtlViewModel ViewModel { get; }
}
