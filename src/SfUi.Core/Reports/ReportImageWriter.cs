using SkiaSharp;

namespace SfUi.Core;

/// <summary>レポート画像の形式。</summary>
public enum ReportImageFormat
{
    Png,
    Jpeg,
    Webp,
}

/// <summary>
/// レポートの表を 1 枚の画像（PNG / JPEG / WebP）へ描画する（SkiaSharp）。
/// ブラウザー不要で、どの環境でも同じ見た目になる。
/// </summary>
public static class ReportImageWriter
{
    private const float Padding = 24f;
    private const float HeaderRowHeight = 24f;
    private const float RowHeight = 20f;
    private const float CellPadding = 8f;
    private const int MaxCanvasWidth = 3200;
    private const int MaxRowsPerTable = 400;

    /// <summary>画像ファイルを書き出す。</summary>
    public static void Write(string path, ReportDocument document, ReportImageFormat format, int maxRowsPerTable = MaxRowsPerTable)
    {
        ArgumentNullException.ThrowIfNull(document);

        var bytes = Render(document, format, maxRowsPerTable);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, bytes);
    }

    /// <summary>画像のバイト列を作る（テスト用）。</summary>
    public static byte[] Render(ReportDocument document, ReportImageFormat format, int maxRowsPerTable = MaxRowsPerTable)
    {
        using var typeface = CreateTypeface();
        using var titlePaint = new SKPaint { Color = SKColor.Parse("#1F2937"), IsAntialias = true, Typeface = typeface, TextSize = 18, FakeBoldText = true };
        using var subtitlePaint = new SKPaint { Color = SKColor.Parse("#6B7280"), IsAntialias = true, Typeface = typeface, TextSize = 11 };
        using var tableTitlePaint = new SKPaint { Color = SKColor.Parse("#111827"), IsAntialias = true, Typeface = typeface, TextSize = 13, FakeBoldText = true };
        using var headerPaint = new SKPaint { Color = SKColor.Parse("#111827"), IsAntialias = true, Typeface = typeface, TextSize = 10, FakeBoldText = true };
        using var cellPaint = new SKPaint { Color = SKColor.Parse("#1F2937"), IsAntialias = true, Typeface = typeface, TextSize = 10 };
        using var notePaint = new SKPaint { Color = SKColor.Parse("#B45309"), IsAntialias = true, Typeface = typeface, TextSize = 10 };
        using var borderPaint = new SKPaint { Color = SKColor.Parse("#D1D5DB"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var headerFill = new SKPaint { Color = SKColor.Parse("#F3F4F6"), Style = SKPaintStyle.Fill };
        using var zebraFill = new SKPaint { Color = SKColor.Parse("#FAFAFA"), Style = SKPaintStyle.Fill };
        using var whiteFill = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };

        var tableCount = document.Tables.Count();
        var tables = document.Tables.Take(6).ToList();
        var rowsPerTable = tables.ToDictionary(t => t, t => Math.Min(t.RowCount, Math.Max(0, maxRowsPerTable)));

        // 列幅を決める（最長テキスト + パディング、最小 60 / 最大 420）
        var widths = new List<float[]>();
        foreach (var table in tables)
        {
            var columnWidths = new float[table.Columns.Count];
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var max = headerPaint.MeasureText(table.Columns[i]) + (CellPadding * 2);
                for (var r = 0; r < rowsPerTable[table]; r++)
                {
                    var text = i < table.Rows[r].Count ? table.Rows[r][i] ?? string.Empty : string.Empty;
                    max = Math.Max(max, cellPaint.MeasureText(text) + (CellPadding * 2));
                }

                columnWidths[i] = Math.Clamp(max, 60f, 420f);
            }

            widths.Add(columnWidths);
        }

        var width = (int)Math.Ceiling(Math.Max(
            480f,
            Math.Min(MaxCanvasWidth, widths.Count == 0 ? 480f : widths.Max(w => w.Sum()) + (Padding * 2))));

        var height = Padding + 30f + 20f;
        for (var t = 0; t < tables.Count; t++)
        {
            var rows = rowsPerTable[tables[t]];
            height += 28f + HeaderRowHeight + (rows * RowHeight) + 20f;
            if (tables[t].RowCount > rows)
            {
                height += 18f;
            }
        }

        height = Math.Min(height + Padding, 30000f);

        using var bitmap = new SKBitmap(Math.Max(1, width), Math.Max(1, (int)height));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var y = Padding;
        canvas.DrawText(Trim(document.Title, titlePaint, width - (Padding * 2)), Padding, y + 16, titlePaint);
        y += 26;

        var meta = string.Join("   ", new[] { document.Subtitle, document.Org, document.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (meta.Length > 0)
        {
            canvas.DrawText(Trim(meta, subtitlePaint, width - (Padding * 2)), Padding, y + 10, subtitlePaint);
            y += 20;
        }

        for (var t = 0; t < tables.Count; t++)
        {
            var table = tables[t];
            var columnWidths = widths[t];
            var rows = rowsPerTable[table];
            var tableWidth = columnWidths.Sum();

            canvas.DrawText(Trim(table.Name, tableTitlePaint, width - (Padding * 2)), Padding, y + 12, tableTitlePaint);
            y += 24;

            // ヘッダー
            var x = Padding;
            canvas.DrawRect(new SKRect(Padding, y, Padding + tableWidth, y + HeaderRowHeight), headerFill);
            for (var i = 0; i < table.Columns.Count; i++)
            {
                canvas.DrawText(Trim(table.Columns[i], headerPaint, columnWidths[i] - (CellPadding * 2)), x + CellPadding, y + 16, headerPaint);
                x += columnWidths[i];
            }

            canvas.DrawRect(new SKRect(Padding, y, Padding + tableWidth, y + HeaderRowHeight), borderPaint);
            y += HeaderRowHeight;

            for (var r = 0; r < rows; r++)
            {
                if (r % 2 == 1)
                {
                    canvas.DrawRect(new SKRect(Padding, y, Padding + tableWidth, y + RowHeight), zebraFill);
                }

                x = Padding;
                var row = table.Rows[r];
                for (var i = 0; i < table.Columns.Count; i++)
                {
                    var text = i < row.Count ? row[i] ?? string.Empty : string.Empty;
                    canvas.DrawText(Trim(text, cellPaint, columnWidths[i] - (CellPadding * 2)), x + CellPadding, y + 14, cellPaint);
                    x += columnWidths[i];
                }

                canvas.DrawRect(new SKRect(Padding, y, Padding + tableWidth, y + RowHeight), borderPaint);
                y += RowHeight;
            }

            if (table.RowCount > rows)
            {
                canvas.DrawText(
                    Trim(UiText.T("Report_ImageTruncated", rows, table.RowCount), notePaint, width - (Padding * 2)),
                    Padding,
                    y + 12,
                    notePaint);
                y += 18;
            }

            y += 20;
        }

        if (tableCount > tables.Count)
        {
            canvas.DrawText(Trim(UiText.T("Report_ImageMoreTables", tables.Count, tableCount), notePaint, width - (Padding * 2)), Padding, y + 10, notePaint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format switch
        {
            ReportImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            ReportImageFormat.Webp => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        }, format == ReportImageFormat.Png ? 100 : 90);

        return data.ToArray();
    }

    private static string Trim(string text, SKPaint paint, float maxWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (paint.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch);
            if (paint.MeasureText(builder.ToString() + "…") > maxWidth)
            {
                builder.Length = Math.Max(0, builder.Length - 1);
                break;
            }
        }

        return builder.ToString() + "…";
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
