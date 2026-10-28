using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>帳票テンプレート出力ダイアログ（テンプレート選択・差し込み・プレビュー・出力・印刷）。</summary>
public partial class FormOutputDialog : Window
{
    public FormOutputDialog(FormOutputViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public FormOutputViewModel ViewModel { get; }

    /// <summary>出力結果（キャンセル時は null）。</summary>
    public ReportExportResult? Result { get; private set; }

    private async void OnRender(object sender, RoutedEventArgs e)
    {
        var result = await ViewModel.RenderInternalAsync().ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        var pages = ViewModel.RenderPrintPages();
        if (pages.Count == 0)
        {
            ViewModel.StatusText = UiText.T("Form_NoPrintPages");
            return;
        }

        var dialog = new System.Windows.Controls.PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var document = new FixedDocument();
        foreach (var png in pages)
        {
            using var stream = new MemoryStream(png);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                Width = dialog.PrintableAreaWidth,
                Height = dialog.PrintableAreaHeight,
            };

            var page = new FixedPage
            {
                Width = dialog.PrintableAreaWidth,
                Height = dialog.PrintableAreaHeight,
            };
            page.Children.Add(image);

            var content = new PageContent();
            content.Child = page;
            document.Pages.Add(content);
        }

        dialog.PrintDocument(document.DocumentPaginator, ViewModel.Title);
        ViewModel.StatusText = UiText.T("Form_PrintedFmt", pages.Count);
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }
}
