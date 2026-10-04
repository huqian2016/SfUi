namespace SfUi.Presentation;

/// <summary>
/// クリップボード操作の抽象化（System.Windows.Clipboard の置き換え）。
/// </summary>
public interface IClipboardService
{
    /// <summary>テキストをクリップボードへコピーする（失敗時は false）。</summary>
    bool TrySetText(string text);
}
