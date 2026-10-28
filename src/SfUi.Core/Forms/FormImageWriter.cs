using System.Globalization;
using SkiaSharp;

namespace SfUi.Core;

/// <summary>帳票を画像（PNG / JPEG）へ描画する（SkiaSharp・1 ページ 1 ファイル）。</summary>
public static class FormImageWriter
{
    /// <summary>既定の解像度（dpi）。</summary>
    public const int DefaultDpi = 150;

    /// <summary>ページを画像のバイト列へ変換する。</summary>
    public static byte[] RenderPage(FormDrawPage page, ReportImageFormat format, int dpi = DefaultDpi)
    {
        ArgumentNullException.ThrowIfNull(page);
        dpi = Math.Clamp(dpi, 72, 600);
        var scale = dpi / 25.4f;
        var width = Math.Max(1, (int)Math.Ceiling(page.WidthMm * scale));
        var height = Math.Max(1, (int)Math.Ceiling(page.HeightMm * scale));

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var typeface = CreateTypeface();
        foreach (var item in page.Items)
        {
            switch (item)
            {
                case FormBoxItem box:
                    DrawBox(canvas, box, scale);
                    break;

                case FormLineItem line:
                    using (var linePaint = new SKPaint
                    {
                        Color = SKColor.Parse(FormDrawList.RowLineColorValue),
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = Math.Max(1f, 0.2f * scale),
                    })
                    {
                        canvas.DrawLine((float)(line.X1 * scale), (float)(line.Y1 * scale),
                            (float)(line.X2 * scale), (float)(line.Y2 * scale), linePaint);
                    }

                    break;

                case FormTextItem text:
                    DrawText(canvas, text, scale, typeface);
                    break;
            }
        }

        using var image = surface.Snapshot();
        using var encoded = image.Encode(
            format == ReportImageFormat.Jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png,
            format == ReportImageFormat.Jpeg ? 92 : 100);
        return encoded.ToArray();
    }

    private static void DrawBox(SKCanvas canvas, FormBoxItem box, float scale)
    {
        var rect = new SKRect((float)(box.X * scale), (float)(box.Y * scale),
            (float)((box.X + box.Width) * scale), (float)((box.Y + box.Height) * scale));

        if (!string.IsNullOrWhiteSpace(box.Fill))
        {
            using var fill = new SKPaint { Color = SKColor.Parse(box.Fill!), Style = SKPaintStyle.Fill, IsAntialias = false };
            canvas.DrawRect(rect, fill);
        }

        if (!string.IsNullOrWhiteSpace(box.Stroke))
        {
            using var stroke = new SKPaint
            {
                Color = SKColor.Parse(box.Stroke!),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1f, 0.2f * scale),
                IsAntialias = true,
            };
            canvas.DrawRect(rect, stroke);
        }
    }

    private static void DrawText(SKCanvas canvas, FormTextItem item, float scale, SKTypeface typeface)
    {
        if (string.IsNullOrEmpty(item.Text))
        {
            return;
        }

        var size = (float)(FormMetrics.PtToMm(item.FontSizePt) * scale);
        using var paint = new SKPaint
        {
            Color = SKColor.Parse(FormDrawList.TextColorValue),
            IsAntialias = true,
            Typeface = typeface,
            TextSize = size,
            FakeBoldText = item.Bold,
        };

        var lines = item.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i];
            var width = paint.MeasureText(text);
            var x = item.Align?.ToLowerInvariant() switch
            {
                "center" => item.X + ((item.Width - (width / scale)) / 2),
                "right" => item.X + item.Width - (width / scale),
                _ => item.X,
            };

            var y = item.Y + (FormMetrics.PtToMm(item.FontSizePt) * 0.95) + (i * FormMetrics.PtToMm(item.FontSizePt) * 1.25);
            canvas.DrawText(text, (float)(x * scale), (float)(y * scale), paint);
        }
    }

    /// <summary>CJK を含むフォントを優先する（Skia はフォールバックしない）。</summary>
    private static SKTypeface CreateTypeface()
    {
        foreach (var name in new[] { "Yu Gothic UI", "Meiryo", "MS Gothic", "Hiragino Sans", "Hiragino Kaku Gothic ProN", "Noto Sans CJK JP", "Segoe UI", "Arial" })
        {
            var typeface = SKTypeface.FromFamilyName(name);
            if (typeface is not null)
            {
                return typeface;
            }
        }

        return SKTypeface.Default;
    }

    /// <summary>mm を px に変換する（テスト用）。</summary>
    internal static double MmToPixel(double mm, int dpi) => mm * dpi / 25.4;

    internal static string FormatMm(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
