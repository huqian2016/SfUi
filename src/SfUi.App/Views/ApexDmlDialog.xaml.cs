using System.Windows;
using SfUi.App.ViewModels;

namespace SfUi.App.Views;

/// <summary>匿名 Apex の DML 文（Insert / Update / Delete）生成ダイアログ。</summary>
public partial class ApexDmlDialog : Window
{
    public ApexDmlDialog()
    {
        InitializeComponent();
    }

    /// <summary>生成された Apex（キャンセル時は null）。</summary>
    public string? Result { get; private set; }

    /// <summary>オブジェクト一覧の読み込みを開始する。</summary>
    public void Initialize()
    {
        if (DataContext is ApexDmlGeneratorViewModel viewModel)
        {
            _ = viewModel.InitializeAsync();
        }
    }

    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ApexDmlGeneratorViewModel viewModel)
        {
            return;
        }

        var error = viewModel.Validate();
        if (error is not null)
        {
            viewModel.StatusText = error;
            return;
        }

        Result = viewModel.Generate();
        DialogResult = true;
    }
}
