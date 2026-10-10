using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>未反映行の背景色を付ける AvalonEdit の行トランスフォーマー（緑 = 追加 / 黄 = 変更）。</summary>
public sealed class SourceLineColorizer : DocumentColorizingTransformer
{
    private static readonly Brush AddedBrush = CreateFrozen(Color.FromArgb(0x30, 0x2E, 0xA0, 0x43));
    private static readonly Brush ModifiedBrush = CreateFrozen(Color.FromArgb(0x38, 0xD9, 0x77, 0x06));

    /// <summary>未反映行（1 始まり行番号 → 種別）。</summary>
    public IReadOnlyDictionary<int, LineChangeKind> ChangedLines { get; set; } = new Dictionary<int, LineChangeKind>();

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0 || !ChangedLines.TryGetValue(line.LineNumber, out var kind))
        {
            return;
        }

        ChangeLinePart(line.Offset, line.EndOffset, element =>
            element.TextRunProperties.SetBackgroundBrush(kind == LineChangeKind.Added ? AddedBrush : ModifiedBrush));
    }

    private static Brush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
