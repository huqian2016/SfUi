using Avalonia.Controls;
using Avalonia.Interactivity;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Dialogs;

/// <summary>匿名 Apex の DML 文（Insert / Update / Delete）生成ダイアログ（Avalonia）。</summary>
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

    private void OnInsert(object? sender, RoutedEventArgs e)
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
        Close(Result);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
