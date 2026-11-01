using System.Windows;
using SfUi.App.ViewModels;
using SfUi.Presentation;

namespace SfUi.App.Views;

/// <summary>接続アプリの作成 / 編集ダイアログ。</summary>
public partial class ConnectedAppEditorDialog : Window
{
    public ConnectedAppEditorDialog()
    {
        InitializeComponent();
    }

    /// <summary>保存が押されたときの結果（キャンセル時は null）。</summary>
    public ConnectedAppEditorResult? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectedAppEditorViewModel viewModel)
        {
            return;
        }

        var result = viewModel.BuildResult();
        if (result is null)
        {
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
