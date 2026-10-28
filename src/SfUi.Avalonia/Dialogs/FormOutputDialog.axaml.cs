using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.Avalonia.Dialogs;

/// <summary>帳票テンプレート出力ダイアログ（Avalonia）。</summary>
public partial class FormOutputDialog : Window
{
    public FormOutputDialog()
    {
        InitializeComponent();
    }

    public FormOutputDialog(FormOutputViewModel viewModel)
        : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public FormOutputViewModel ViewModel { get; } = null!;

    /// <summary>出力結果（キャンセル時は null）。</summary>
    public ReportExportResult? Result { get; private set; }

    private async void OnRender(object? sender, RoutedEventArgs e)
    {
        var result = await ViewModel.RenderInternalAsync().ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        Result = result;
        Close(result);
    }

    /// <summary>
    /// Avalonia には印刷 API が無いため、出力した PDF / HTML を既定のアプリで開いて印刷してもらう。
    /// </summary>
    private async void OnPrint(object? sender, RoutedEventArgs e)
    {
        var result = await ViewModel.RenderInternalAsync().ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        var target = result.Files.FirstOrDefault(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            ?? result.Files.FirstOrDefault(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            ?? result.Files.FirstOrDefault();

        if (target is null)
        {
            ViewModel.StatusText = UiText.T("Form_NoPrintPages");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = UiText.T("Form_OpenFailedFmt", ex.Message);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
