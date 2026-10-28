namespace SfUi.Core;

/// <summary>帳票の単位変換（帳票要素のフォント サイズは pt で持つ）。</summary>
public static class FormMetrics
{
    /// <summary>1pt = 0.3528mm。</summary>
    public const double MmPerPoint = 25.4 / 72.0;

    /// <summary>pt を mm へ変換する。</summary>
    public static double PtToMm(double points) => points * MmPerPoint;

    /// <summary>pt をピクセルへ変換する（dpi 指定）。</summary>
    public static double PtToPixel(double points, double dpi) => PtToMm(points) * dpi / 25.4;
}
