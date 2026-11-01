using Avalonia.Controls;
using SfUi.App.ViewModels;
using SfUi.Avalonia.Dialogs;
using SfUi.Presentation;

namespace SfUi.Avalonia.Services;

/// <summary>接続アプリの作成 / 編集ダイアログ（Avalonia）。</summary>
public sealed class AvaloniaConnectedAppEditor : IConnectedAppEditorDialog
{
    private readonly TopLevelAccessor _top;

    public AvaloniaConnectedAppEditor(TopLevelAccessor top)
    {
        _top = top;
    }

    public async Task<ConnectedAppEditorResult?> EditAsync(ConnectedAppEditorRequest request)
    {
        var viewModel = new ConnectedAppEditorViewModel(request);
        var dialog = new ConnectedAppEditorDialog { DataContext = viewModel };
        if (_top.Current is Window owner)
        {
            return await dialog.ShowDialog<ConnectedAppEditorResult?>(owner);
        }

        dialog.Show();
        return null;
    }
}
