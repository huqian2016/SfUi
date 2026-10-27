namespace SfUi.Core;

/// <summary>
/// 通知ポップアップの描画位置計算（UI 非依存の純粋関数。テスト可能にするため Core に置く）。
/// </summary>
public static class NotificationPlacement
{
    /// <summary>作業領域の右下に配置するための Left / Top を返す。</summary>
    public static (double Left, double Top) BottomRight(
        double workAreaLeft,
        double workAreaTop,
        double workAreaWidth,
        double workAreaHeight,
        double windowWidth,
        double windowHeight,
        double margin = 12)
    {
        var left = workAreaLeft + Math.Max(0, workAreaWidth - windowWidth - margin);
        var top = workAreaTop + Math.Max(0, workAreaHeight - windowHeight - margin);
        return (left, top);
    }
}
