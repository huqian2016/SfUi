using SfUi.Presentation;

namespace SfUi.Avalonia.Services;

/// <summary>Avalonia のクリップボード API を使う IClipboardService 実装。</summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    private readonly TopLevelAccessor _top;

    public AvaloniaClipboardService(TopLevelAccessor top) => _top = top;

    public bool TrySetText(string text)
    {
        var clipboard = _top.Current?.Clipboard;
        if (clipboard is null)
        {
            return false;
        }

        _ = clipboard.SetTextAsync(text);
        return true;
    }
}
