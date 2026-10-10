using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using SfUi.Core;

namespace SfUi.Avalonia.Views;

/// <summary>未反映行の背景色を付ける AvaloniaEdit の行トランスフォーマー（緑 = 追加 / 黄 = 変更）。</summary>
public sealed class SourceLineColorizer : DocumentColorizingTransformer
{
    private static readonly IBrush AddedBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x30, 0x2E, 0xA0, 0x43));
    private static readonly IBrush ModifiedBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x38, 0xD9, 0x77, 0x06));

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
}
