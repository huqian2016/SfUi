using System.Windows;
using SfUi.App.ViewModels;
using SfUi.App.Views;
using SfUi.Presentation;

namespace SfUi.App.Services;

/// <summary>接続アプリの作成 / 編集ダイアログ（WPF）。</summary>
public sealed class WpfConnectedAppEditor : IConnectedAppEditorDialog
{
    public Task<ConnectedAppEditorResult?> EditAsync(ConnectedAppEditorRequest request)
    {
        var viewModel = new ConnectedAppEditorViewModel(request);
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
        var dialog = new ConnectedAppEditorDialog
        {
            DataContext = viewModel,
            Owner = owner,
        };

        var ok = dialog.ShowDialog() == true;
        return Task.FromResult(ok ? dialog.Result : null);
    }
}
