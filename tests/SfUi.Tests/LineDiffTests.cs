using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>未反映行ハイライト用の行 diff（LineDiff）のテスト。</summary>
public class LineDiffTests
{
    private static string[] Changed(string baseline, string working)
        => LineDiff.Compute(baseline, working).OrderBy(kv => kv.Key).Select(kv => kv.Key + ":" + kv.Value).ToArray();

    [Fact]
    public void Identical_ReturnsEmpty()
    {
        Assert.Empty(LineDiff.Compute("a\nb\nc", "a\nb\nc"));
        Assert.Empty(LineDiff.Compute("", ""));
    }

    [Fact]
    public void ModifiedLine_IsModified()
    {
        Assert.Equal(new[] { "2:Modified" }, Changed("a\nb\nc", "a\nB\nc"));
    }

    [Fact]
    public void InsertedLine_IsAdded()
    {
        Assert.Equal(new[] { "2:Added" }, Changed("a\nc", "a\nnew\nc"));
    }

    [Fact]
    public void Replacement_InsertedLinesAreModified()
    {
        Assert.Equal(new[] { "2:Modified" }, Changed("a\nx\nc", "a\ny\nc"));
    }

    [Fact]
    public void DeletionOnly_MarksBoundaryLine()
    {
        // 2 行目を削除 → 直前の行（1 行目）に境界マーカー
        Assert.Equal(new[] { "1:Modified" }, Changed("a\nb\nc", "a\nc"));
    }

    [Fact]
    public void DeletionAtTop_MarksFirstLine()
    {
        Assert.Equal(new[] { "1:Modified" }, Changed("a\nb", "b"));
    }

    [Fact]
    public void AppendLines_AreAdded()
    {
        Assert.Equal(new[] { "3:Added", "4:Added" }, Changed("a\nb", "a\nb\nc\nd"));
    }

    [Fact]
    public void TwoDistantEdits_MarkBothRegions()
    {
        var baseline = "l1\nl2\nl3\nl4\nl5\nl6\nl7\nl8";
        var working = "l1\nL2\nl3\nl4\nl5\nl6\nl7\nL8";
        Assert.Equal(new[] { "2:Modified", "8:Modified" }, Changed(baseline, working));
    }

    [Fact]
    public void EmptyBaseline_AllLinesAdded()
    {
        Assert.Equal(new[] { "1:Added", "2:Added" }, Changed("", "x\ny"));
    }

    [Fact]
    public void EmptyWorking_TrailingDeletion_Anchors()
    {
        // 全削除は working に行が無いためマーカーなし
        Assert.Empty(LineDiff.Compute("a\nb", ""));
    }

    [Fact]
    public void TrailingNewlineVariants_AreEqual()
    {
        Assert.Empty(LineDiff.Compute("a\nb\n", "a\nb"));
        Assert.Empty(LineDiff.Compute("a\r\nb", "a\nb"));
    }

    [Fact]
    public void SplitLines_HandlesCrLfAndTrailingNewline()
    {
        Assert.Equal(new[] { "a", "b" }, LineDiff.SplitLines("a\r\nb\n"));
        Assert.Equal(new[] { "a", "" }, LineDiff.SplitLines("a\n\n"));
        Assert.Empty(LineDiff.SplitLines(""));
        Assert.Empty(LineDiff.SplitLines(null));
    }
}
