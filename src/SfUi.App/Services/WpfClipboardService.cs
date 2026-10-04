using System.Windows;
using SfUi.Presentation;

namespace SfUi.App.Services;

/// <summary>WPF のクリップボード API を使う IClipboardService 実装。</summary>
public sealed class WpfClipboardService : IClipboardService
{
    public bool TrySetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            // クリップボードが他プロセスにロックされている場合など
            return false;
        }
    }
}
