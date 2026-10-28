using System.Globalization;
using SkiaSharp;

namespace SfUi.Core;

/// <summary>
/// レポートのグラフ（縦棒 / 横棒 / 折れ線 / 円 / ドーナツ）を PNG 画像へ描画する（SkiaSharp）。
/// ブラウザー不要で、どの環境でも同じ見た目になる。
/// </summary>
public static class ReportChartRenderer
{
    private const float Padding = 16f;
    private const float TitleHeight = 26f;
    private const float AxisLabelSize = 10f;
    private const float LegendSize = 11f;
    private const float BarGap = 0.25f;

    private static readonly SKColor[] Palette =
    {
        SKColor.Parse("#2563EB"),
        SKColor.Parse("#F59E0B"),
        SKColor.Parse("#10B981"),
        SKColor.Parse("#EF4444"),
        SKColor.Parse("#8B5CF6"),
        SKColor.Parse("#0EA5E9"),
        SKColor.Parse("#F472B6"),
        SKColor.Parse("#84CC16"),
    };

    private static readonly SKColor TextColor = SKColor.Parse("#1F2937");
    private static readonly SKColor MutedColor = SKColor.Parse("#6B7280");
    private static readonly SKColor GridColor = SKColor.Parse("#E5E7EB");
    private static readonly SKColor AxisColor = SKColor.Parse("#9CA3AF");

    /// <summary>グラフを PNG バイト列にする。</summary>
    public static byte[] RenderPng(ReportChartData data, int width = 960, int height = 420)
    {
        ArgumentNullException.ThrowIfNull(data);

        width = Math.Max(240, width);
        height = Math.Max(160, height);

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var typeface = CreateTypeface();
        using var titlePaint = new SKPaint { Color = TextColor, IsAntialias = true, Typeface = typeface, TextSize = 14, FakeBoldText = true };
        using var labelPaint = new SKPaint { Color = MutedColor, IsAntialias = true, Typeface = typeface, TextSize = AxisLabelSize };
        using var valuePaint = new SKPaint { Color = TextColor, IsAntialias = true, Typeface = typeface, TextSize = AxisLabelSize };
        using var legendPaint = new SKPaint { Color = TextColor, IsAntialias = true, Typeface = typeface, TextSize = LegendSize };
        using var gridPaint = new SKPaint { Color = GridColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var axisPaint = new SKPaint { Color = AxisColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        var top = Padding;
        if (!string.IsNullOrWhiteSpace(data.Title))
        {
            canvas.DrawText(Trim(data.Title, titlePaint, width - (Padding * 2)), Padding, top + 14, titlePaint);
            top += TitleHeight;
        }

        var plot = new SKRect(Padding, top, width - Padding, height - Padding);
        if (ReportChartTypes.IsCircular(data.Type))
        {
            DrawCircular(canvas, data, plot, fillPaint, labelPaint, legendPaint, valuePaint);
        }
        else if (string.Equals(data.Type, ReportChartTypes.Bar, StringComparison.OrdinalIgnoreCase))
        {
            DrawBars(canvas, data, plot, true, fillPaint, gridPaint, axisPaint, labelPaint, valuePaint, legendPaint);
        }
        else if (string.Equals(data.Type, ReportChartTypes.Line, StringComparison.OrdinalIgnoreCase))
        {
            DrawLine(canvas, data, plot, fillPaint, gridPaint, axisPaint, labelPaint, valuePaint, legendPaint);
        }
        else
        {
            DrawBars(canvas, data, plot, false, fillPaint, gridPaint, axisPaint, labelPaint, valuePaint, legendPaint);
        }

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    /// <summary>グラフを PNG ファイルへ書き出す。</summary>
    public static void WritePng(string path, ReportChartData data, int width = 960, int height = 420)
    {
        var bytes = RenderPng(data, width, height);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, bytes);
    }

    /// <summary>縦棒 / 横棒グラフ。</summary>
    private static void DrawBars(
        SKCanvas canvas,
        ReportChartData data,
        SKRect plot,
        bool horizontal,
        SKPaint fill,
        SKPaint grid,
        SKPaint axis,
        SKPaint labelPaint,
        SKPaint valuePaint,
        SKPaint legendPaint)
    {
        var legendHeight = data.ShowLegend && data.Series.Count > 1 ? 20f : 0f;
        var axisSize = 56f;
        var chart = horizontal
            ? new SKRect(plot.Left + axisSize + 60, plot.Top, plot.Right, plot.Bottom - legendHeight - 8)
            : new SKRect(plot.Left + axisSize, plot.Top, plot.Right, plot.Bottom - legendHeight - 18);

        var max = NiceMax(Math.Max(0.0001, data.MaxValue));
        var min = data.MinValue < 0 ? -NiceMax(-data.MinValue) : 0;

        // 目盛り
        for (var i = 0; i <= 4; i++)
        {
            var value = min + ((max - min) * i / 4);
            if (horizontal)
            {
                var x = chart.Left + (chart.Width * i / 4);
                canvas.DrawLine(x, chart.Top, x, chart.Bottom, grid);
                canvas.DrawText(Trim(FormatValue(value), labelPaint, 54), x - 26, chart.Bottom + 14, labelPaint);
            }
            else
            {
                var y = chart.Bottom - (chart.Height * i / 4);
                canvas.DrawLine(chart.Left, y, chart.Right, y, grid);
                canvas.DrawText(Trim(FormatValue(value), labelPaint, axisSize - 6), Padding, y + 3, labelPaint);
            }
        }

        canvas.DrawLine(chart.Left, chart.Top, chart.Left, chart.Bottom, axis);
        canvas.DrawLine(chart.Left, chart.Bottom, chart.Right, chart.Bottom, axis);

        var categories = data.Categories.Count;
        if (categories == 0)
        {
            return;
        }

        var seriesCount = data.Series.Count;
        var slot = (horizontal ? chart.Height : chart.Width) / categories;
        var barThickness = slot * (1 - BarGap) / Math.Max(1, seriesCount);

        for (var c = 0; c < categories; c++)
        {
            var slotStart = (horizontal ? chart.Top : chart.Left) + (slot * c);
            for (var s = 0; s < seriesCount; s++)
            {
                var series = data.Series[s];
                var value = c < series.Values.Count ? series.Values[c] : 0;
                var ratio = (float)((value - min) / (max - min));
                fill.Color = Palette[s % Palette.Length];

                if (horizontal)
                {
                    var y = slotStart + (slot * BarGap / 2) + (barThickness * s);
                    var x = chart.Left + (chart.Width * Math.Max(0f, ratio));
                    canvas.DrawRect(new SKRect(Math.Min(chart.Left, x), y, Math.Max(chart.Left, x), y + barThickness - 1), fill);
                }
                else
                {
                    var x = slotStart + (slot * BarGap / 2) + (barThickness * s);
                    var zeroY = chart.Bottom - (chart.Height * (float)((0 - min) / (max - min)));
                    var y = chart.Bottom - (chart.Height * ratio);
                    canvas.DrawRect(new SKRect(x, Math.Min(zeroY, y), x + barThickness - 1, Math.Max(zeroY, y)), fill);
                }
            }

            // カテゴリ名
            var label = Trim(data.Categories[c], labelPaint, horizontal ? chart.Left - Padding - 8 : Math.Max(24, slot - 4));
            if (horizontal)
            {
                canvas.DrawText(label, Padding, slotStart + (slot / 2) + 3, labelPaint);
            }
            else
            {
                var textWidth = labelPaint.MeasureText(label);
                canvas.DrawText(label, slotStart + ((slot - textWidth) / 2), chart.Bottom + 13, labelPaint);
            }
        }

        if (data.ShowLegend && seriesCount > 1)
        {
            DrawLegend(canvas, data, new SKRect(plot.Left, plot.Bottom - legendHeight, plot.Right, plot.Bottom), fill, legendPaint);
        }
    }

    /// <summary>折れ線グラフ。</summary>
    private static void DrawLine(
        SKCanvas canvas,
        ReportChartData data,
        SKRect plot,
        SKPaint fill,
        SKPaint grid,
        SKPaint axis,
        SKPaint labelPaint,
        SKPaint valuePaint,
        SKPaint legendPaint)
    {
        var legendHeight = data.ShowLegend ? 20f : 0f;
        var axisSize = 56f;
        var chart = new SKRect(plot.Left + axisSize, plot.Top, plot.Right, plot.Bottom - legendHeight - 18);
        var max = NiceMax(Math.Max(0.0001, data.MaxValue));
        var min = data.MinValue < 0 ? -NiceMax(-data.MinValue) : 0;

        for (var i = 0; i <= 4; i++)
        {
            var value = min + ((max - min) * i / 4);
            var y = chart.Bottom - (chart.Height * i / 4);
            canvas.DrawLine(chart.Left, y, chart.Right, y, grid);
            canvas.DrawText(Trim(FormatValue(value), labelPaint, axisSize - 6), Padding, y + 3, labelPaint);
        }

        canvas.DrawLine(chart.Left, chart.Top, chart.Left, chart.Bottom, axis);
        canvas.DrawLine(chart.Left, chart.Bottom, chart.Right, chart.Bottom, axis);

        var count = data.Categories.Count;
        if (count == 0)
        {
            return;
        }

        var step = count == 1 ? 0 : chart.Width / (count - 1);
        using var linePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };

        for (var s = 0; s < data.Series.Count; s++)
        {
            var series = data.Series[s];
            linePaint.Color = Palette[s % Palette.Length];
            using var path = new SKPath();
            for (var c = 0; c < count; c++)
            {
                var value = c < series.Values.Count ? series.Values[c] : 0;
                var x = chart.Left + (step * c);
                var y = chart.Bottom - (chart.Height * (float)((value - min) / (max - min)));
                if (c == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            canvas.DrawPath(path, linePaint);
            fill.Color = Palette[s % Palette.Length];
            for (var c = 0; c < count; c++)
            {
                var value = c < series.Values.Count ? series.Values[c] : 0;
                var x = chart.Left + (step * c);
                var y = chart.Bottom - (chart.Height * (float)((value - min) / (max - min)));
                canvas.DrawCircle(x, y, 3, fill);
            }
        }

        // カテゴリ名（詰まっていれば間引く）
        var maxLabels = Math.Max(1, (int)(chart.Width / 60));
        var stride = Math.Max(1, (int)Math.Ceiling(count / (double)maxLabels));
        for (var c = 0; c < count; c += stride)
        {
            var label = Trim(data.Categories[c], labelPaint, 58);
            var textWidth = labelPaint.MeasureText(label);
            canvas.DrawText(label, chart.Left + (step * c) - (textWidth / 2), chart.Bottom + 13, labelPaint);
        }

        if (data.ShowLegend)
        {
            DrawLegend(canvas, data, new SKRect(plot.Left, plot.Bottom - legendHeight, plot.Right, plot.Bottom), fill, legendPaint);
        }
    }

    /// <summary>円 / ドーナツ グラフ。</summary>
    private static void DrawCircular(
        SKCanvas canvas,
        ReportChartData data,
        SKRect plot,
        SKPaint fill,
        SKPaint labelPaint,
        SKPaint legendPaint,
        SKPaint valuePaint)
    {
        var series = data.Series.FirstOrDefault();
        if (series is null || data.Categories.Count == 0)
        {
            return;
        }

        var total = data.Categories.Select((_, i) => Math.Max(0, i < series.Values.Count ? series.Values[i] : 0)).Sum();
        if (total <= 0)
        {
            return;
        }

        var legendWidth = data.ShowLegend ? Math.Min(plot.Width * 0.32f, 200f) : 0f;
        var size = Math.Min(plot.Width - legendWidth, plot.Height);
        var rect = new SKRect(
            plot.Left,
            plot.Top + ((plot.Height - size) / 2),
            plot.Left + size,
            plot.Top + ((plot.Height - size) / 2) + size);

        var isDonut = string.Equals(data.Type, ReportChartTypes.Donut, StringComparison.OrdinalIgnoreCase);
        var start = -90f;
        for (var i = 0; i < data.Categories.Count; i++)
        {
            var value = Math.Max(0, i < series.Values.Count ? series.Values[i] : 0);
            var sweep = (float)(value / total * 360);
            fill.Color = Palette[i % Palette.Length];
            using var path = new SKPath();
            path.MoveTo(rect.MidX, rect.MidY);
            path.ArcTo(rect, start, sweep, false);
            path.Close();
            canvas.DrawPath(path, fill);

            if (sweep >= 18)
            {
                var mid = (start + (sweep / 2)) * Math.PI / 180;
                var radius = size * (isDonut ? 0.42f : 0.3f);
                var labelX = rect.MidX + ((float)Math.Cos(mid) * radius);
                var labelY = rect.MidY + ((float)Math.Sin(mid) * radius);
                var text = (value / total * 100).ToString("0", CultureInfo.CurrentCulture) + "%";
                canvas.DrawText(text, labelX - (valuePaint.MeasureText(text) / 2), labelY + 3, valuePaint);
            }

            start += sweep;
        }

        if (isDonut)
        {
            using var hole = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawCircle(rect.MidX, rect.MidY, size * 0.28f, hole);
        }

        if (!data.ShowLegend)
        {
            return;
        }

        var legendX = rect.Right + 18;
        var legendY = plot.Top + 6;
        for (var i = 0; i < data.Categories.Count && i < 12; i++)
        {
            fill.Color = Palette[i % Palette.Length];
            canvas.DrawRect(new SKRect(legendX, legendY, legendX + 10, legendY + 10), fill);
            var value = i < series.Values.Count ? series.Values[i] : 0;
            var label = Trim(
                $"{data.Categories[i]} ({FormatValue(value)})",
                legendPaint,
                Math.Max(40f, plot.Right - legendX - 14));
            canvas.DrawText(label, legendX + 14, legendY + 9, legendPaint);
            legendY += 17;
        }
    }

    private static void DrawLegend(SKCanvas canvas, ReportChartData data, SKRect area, SKPaint fill, SKPaint textPaint)
    {
        var x = area.Left;
        var y = area.Top + 12;
        for (var s = 0; s < data.Series.Count; s++)
        {
            fill.Color = Palette[s % Palette.Length];
            canvas.DrawRect(new SKRect(x, y - 8, x + 10, y + 2), fill);
            var name = data.Series[s].Name;
            canvas.DrawText(name, x + 14, y + 1, textPaint);
            x += 22 + textPaint.MeasureText(name);
        }
    }

    /// <summary>0.30 などの数値を 0.3 に、1000 以上は 1.0K に整形する。</summary>
    internal static string FormatValue(double value)
    {
        var absolute = Math.Abs(value);
        if (absolute >= 1_000_000_000)
        {
            return (value / 1_000_000_000).ToString("0.#", CultureInfo.CurrentCulture) + "B";
        }

        if (absolute >= 1_000_000)
        {
            return (value / 1_000_000).ToString("0.#", CultureInfo.CurrentCulture) + "M";
        }

        if (absolute >= 1_000)
        {
            return (value / 1_000).ToString("0.#", CultureInfo.CurrentCulture) + "K";
        }

        return absolute >= 10 || value == Math.Floor(value)
            ? value.ToString("N0", CultureInfo.CurrentCulture)
            : value.ToString("0.##", CultureInfo.CurrentCulture);
    }

    /// <summary>軸の最大値を切りの良い値へ切り上げる。</summary>
    internal static double NiceMax(double value)
    {
        if (value <= 0)
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalized = value / magnitude;
        var nice = normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 2.5 => 2.5,
            <= 5 => 5,
            _ => 10,
        };

        return nice * magnitude;
    }

    private static string Trim(string text, SKPaint paint, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || paint.MeasureText(text) <= maxWidth)
        {
            return text ?? string.Empty;
        }

        var trimmed = text;
        while (trimmed.Length > 1 && paint.MeasureText(trimmed + "…") > maxWidth)
        {
            trimmed = trimmed[..^1];
        }

        return trimmed + "…";
    }

    private static SKTypeface CreateTypeface()
    {
        // CJK を含むフォントを優先する（Skia はフォールバックしないため、日本語が豆腐になるのを防ぐ）。
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
}
