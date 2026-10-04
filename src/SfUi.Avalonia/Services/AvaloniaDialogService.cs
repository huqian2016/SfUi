using Avalonia.Controls;
using SfUi.Avalonia.Dialogs;
using SfUi.Presentation;

namespace SfUi.Avalonia.Services;

/// <summary>Avalonia の共通ダイアログを使う IDialogService 実装。</summary>
public sealed class AvaloniaDialogService : IDialogService
{
    private readonly TopLevelAccessor _top;

    public AvaloniaDialogService(TopLevelAccessor top) => _top = top;

    public void Info(string message, string caption) => MessageDialog.Info(Owner(), message, caption);

    public void Warning(string message, string caption) => MessageDialog.Info(Owner(), message, caption);

    public bool Confirm(string message, string caption) => MessageDialog.Confirm(Owner(), message, caption, destructive: false);

    public bool ConfirmDestructive(string message, string caption) => MessageDialog.Confirm(Owner(), message, caption, destructive: true);

    public string? Prompt(string title, string prompt, string defaultValue = "") =>
        MessageDialog.Prompt(Owner(), title, prompt, defaultValue);

    private Window? Owner() => _top.Current as Window;
}
