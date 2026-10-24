using System.Globalization;
using System.Text;
using SkiaSharp;

namespace SfUi.Core;

/// <summary>ER 図の画像形式。</summary>
public enum ErdImageFormat
{
    Svg,
    Png,
    Jpeg,
    Webp,
}

/// <summary>ER 図画像のレイアウト / 画質オプション。</summary>
public sealed class ErdImageOptions
{
    public int NodeWidth { get; init; } = 280;

    /// <summary>0 = 自動（エンティティ数の平方根）。</summary>
    public int Columns { get; init; }

    /// <summary>ラスター画像の倍率（2 = Retina 相当）。</summary>
    public int Scale { get; init; } = 2;

    public int MaxFieldsPerNode { get; init; } = 12;
}

/// <summary>画像 / SVG のレイアウト結果（描画前の座標計算）。</summary>
public sealed class ErdImageLayout
{
    public required IReadOnlyList<ErdNode> Nodes { get; init; }

    public required IReadOnlyList<ErdEdge> Edges { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public sealed class ErdNode
    {
        public required ErdEntity Entity { get; init; }

        public required IReadOnlyList<ErdField> Fields { get; init; }

        public required float X { get; init; }

        public required float Y { get; init; }

        public required float Width { get; init; }

        public required float Height { get; init; }
    }

    public sealed class ErdEdge
    {
        public required ErdNode From { get; init; }

        public required ErdNode To { get; init; }

        public required ErdRelationship Relationship { get; init; }

        public required IReadOnlyList<(float X, float Y)> Points { get; init; }
    }
}

/// <summary>
/// ER 図を SVG（テキスト）または PNG / JPEG / WebP（SkiaSharp）へ描画する。
/// レイアウトはグリッド配置 + 直交エッジのシンプルな自動レイアウト。
/// </summary>
public static class ErdImageRenderer
{
    private const float HeaderHeight = 34;
    private const float RowHeight = 18;
    private const float Padding = 10;
    private const float Margin = 40;
    private const float GapX = 90;
    private const float GapY = 70;
    private const string BorderColor = "#4A6FA5";
    private const string HeaderColor = "#E8EEF8";
    private const string TextColor = "#1F2937";
    private const string SubTextColor = "#4B5563";
    private const string ForeignKeyColor = "#B45309";

    public static string FileExtension(ErdImageFormat format) => format switch
    {
        ErdImageFormat.Svg => "svg",
        ErdImageFormat.Png => "png",
        ErdImageFormat.Jpeg => "jpg",
        ErdImageFormat.Webp => "webp",
        _ => "png",
    };

    public static string FormatLabel(ErdImageFormat format) => format switch
    {
        ErdImageFormat.Svg => "SVG",
        ErdImageFormat.Png => "PNG",
        ErdImageFormat.Jpeg => "JPEG",
        ErdImageFormat.Webp => "WebP",
        _ => format.ToString(),
    };

    public static byte[] Render(ErdDocument document, ErdImageFormat format, ErdImageOptions? options = null)
    {
        options ??= new ErdImageOptions();
        return format switch
        {
            ErdImageFormat.Svg => Encoding.UTF8.GetBytes(WriteSvg(document, options)),
            ErdImageFormat.Png => RenderRaster(document, options, SKEncodedImageFormat.Png, 100),
            ErdImageFormat.Jpeg => RenderRaster(document, options, SKEncodedImageFormat.Jpeg, 90),
            ErdImageFormat.Webp => RenderRaster(document, options, SKEncodedImageFormat.Webp, 90),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown image format."),
        };
    }

    // ------------------------------------------------------------- layout

    public static ErdImageLayout BuildLayout(ErdDocument document, ErdImageOptions? options = null)
    {
        options ??= new ErdImageOptions();
        var entities = document.Entities;
        var count = Math.Max(1, entities.Count);
        var columns = options.Columns > 0 ? options.Columns : (int)Math.Ceiling(Math.Sqrt(count));
        columns = Math.Max(1, Math.Min(columns, count));

        var nodes = new List<ErdImageLayout.ErdNode>();
        var rows = new List<List<ErdImageLayout.ErdNode>>();
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            var fields = entity.Fields
                .OrderByDescending(field => field.IsPrimaryKey)
                .ThenByDescending(field => field.IsLookup)
                .ThenBy(field => field.ApiName, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, options.MaxFieldsPerNode))
                .ToList();
            var height = HeaderHeight + (fields.Count * RowHeight) + (Padding * 2);
            var column = i % columns;
            var row = i / columns;
            var node = new ErdImageLayout.ErdNode
            {
                Entity = entity,
                Fields = fields,
                X = Margin + (column * (options.NodeWidth + GapX)),
                Y = 0,
                Width = options.NodeWidth,
                Height = height,
            };

            if (rows.Count <= row)
            {
                rows.Add(new List<ErdImageLayout.ErdNode>());
            }

            rows[row].Add(node);
            nodes.Add(node);
        }

        var y = Margin;
        foreach (var row in rows)
        {
            var rowHeight = row.Max(node => node.Height);
            foreach (var node in row)
            {
                nodes[nodes.IndexOf(node)] = new ErdImageLayout.ErdNode
                {
                    Entity = node.Entity,
                    Fields = node.Fields,
                    X = node.X,
                    Y = y,
                    Width = node.Width,
                    Height = node.Height,
                };
            }

            y += rowHeight + GapY;
        }

        var byName = nodes.ToDictionary(node => node.Entity.ApiName, StringComparer.OrdinalIgnoreCase);
        var edges = new List<ErdImageLayout.ErdEdge>();
        foreach (var relationship in document.Relationships)
        {
            if (!byName.TryGetValue(relationship.ParentApiName, out var parent) ||
                !byName.TryGetValue(relationship.ChildApiName, out var child))
            {
                continue;
            }

            edges.Add(new ErdImageLayout.ErdEdge
            {
                From = parent,
                To = child,
                Relationship = relationship,
                Points = RouteEdge(parent, child),
            });
        }

        var totalWidth = (int)Math.Ceiling(nodes.Count == 0 ? 600 : nodes.Max(node => node.X + node.Width) + Margin);
        var totalHeight = (int)Math.Ceiling(nodes.Count == 0 ? 300 : nodes.Max(node => node.Y + node.Height) + Margin);
        return new ErdImageLayout { Nodes = nodes, Edges = edges, Width = totalWidth, Height = totalHeight };
    }

    private static List<(float X, float Y)> RouteEdge(ErdImageLayout.ErdNode parent, ErdImageLayout.ErdNode child)
    {
        var points = new List<(float X, float Y)>();
        if (ReferenceEquals(parent, child))
        {
            var right = parent.X + parent.Width;
            var top = parent.Y + 20;
            points.Add((right, top));
            points.Add((right + 40, top));
            points.Add((right + 40, parent.Y + parent.Height + 20));
            points.Add((parent.X + (parent.Width / 2), parent.Y + parent.Height + 20));
            points.Add((parent.X + (parent.Width / 2), parent.Y + parent.Height));
            return points;
        }

        var startX = parent.X + parent.Width;
        var startY = parent.Y + 20;
        var endX = child.X;
        var endY = child.Y + 20;
        if (child.X < parent.X)
        {
            startX = parent.X;
            endX = child.X + child.Width;
        }

        var midX = startX + ((endX - startX) / 2);
        points.Add((startX, startY));
        points.Add((midX, startY));
        points.Add((midX, endY));
        points.Add((endX, endY));
        return points;
    }

    // ---------------------------------------------------------------- SVG

    public static string WriteSvg(ErdDocument document, ErdImageOptions? options = null)
    {
        options ??= new ErdImageOptions();
        var layout = BuildLayout(document, options);
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(layout.Width)
            .Append("\" height=\"").Append(layout.Height).Append("\" viewBox=\"0 0 ")
            .Append(layout.Width).Append(' ').Append(layout.Height).AppendLine("\" font-family=\"Segoe UI, Yu Gothic UI, Hiragino Sans, sans-serif\">");
        builder.Append("  <title>").Append(EscapeXml(document.Title)).AppendLine("</title>");
        builder.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"#FFFFFF\" />");

        builder.AppendLine("  <g id=\"edges\" fill=\"none\" stroke=\"" + BorderColor + "\" stroke-width=\"1.6\">");
        foreach (var edge in layout.Edges)
        {
            builder.Append("    <path d=\"M ").Append(Format(edge.Points));
            builder.Append("\" marker-end=\"url(#crow)\" />").AppendLine();
            var last = edge.Points[^1];
            builder.Append("    <text x=\"").Append(Number(last.X + 6)).Append("\" y=\"").Append(Number(last.Y - 4))
                .Append("\" font-size=\"10\" fill=\"").Append(SubTextColor).Append("\" stroke=\"none\">")
                .Append(EscapeXml(edge.Relationship.FieldApiName)).AppendLine("</text>");
        }

        builder.AppendLine("  </g>");

        builder.AppendLine("  <g id=\"nodes\">");
        foreach (var node in layout.Nodes)
        {
            builder.Append("    <g id=\"").Append(EscapeXml(node.Entity.ApiName)).AppendLine("\">");
            builder.Append("      <rect x=\"").Append(Number(node.X)).Append("\" y=\"").Append(Number(node.Y))
                .Append("\" width=\"").Append(Number(node.Width)).Append("\" height=\"").Append(Number(node.Height))
                .Append("\" rx=\"8\" fill=\"#FFFFFF\" stroke=\"").Append(BorderColor).AppendLine("\" stroke-width=\"1.6\" />");
            builder.Append("      <path d=\"M ").Append(Number(node.X)).Append(' ').Append(Number(node.Y + HeaderHeight))
                .Append(" H ").Append(Number(node.X + node.Width)).Append(" V ").Append(Number(node.Y + 8))
                .Append(" Q ").Append(Number(node.X + node.Width)).Append(' ').Append(Number(node.Y))
                .Append(' ').Append(Number(node.X + node.Width - 8)).Append(' ').Append(Number(node.Y))
                .Append(" H ").Append(Number(node.X + 8))
                .Append(" Q ").Append(Number(node.X)).Append(' ').Append(Number(node.Y))
                .Append(' ').Append(Number(node.X)).Append(' ').Append(Number(node.Y + 8))
                .Append(" Z\" fill=\"").Append(HeaderColor).AppendLine("\" />");
            builder.Append("      <text x=\"").Append(Number(node.X + 10)).Append("\" y=\"").Append(Number(node.Y + 22))
                .Append("\" font-size=\"13\" font-weight=\"600\" fill=\"").Append(TextColor).Append("\">")
                .Append(EscapeXml(node.Entity.DisplayName(document.NameStyle))).AppendLine("</text>");

            for (var i = 0; i < node.Fields.Count; i++)
            {
                var field = node.Fields[i];
                var textY = node.Y + HeaderHeight + 6 + ((i + 1) * RowHeight) - 4;
                if (field.IsPrimaryKey || field.IsLookup)
                {
                    builder.Append("      <text x=\"").Append(Number(node.X + 10)).Append("\" y=\"").Append(Number(textY))
                        .Append("\" font-size=\"9\" fill=\"").Append(field.IsPrimaryKey ? BorderColor : ForeignKeyColor)
                        .Append("\">").Append(field.IsPrimaryKey ? "PK" : "FK").AppendLine("</text>");
                }

                builder.Append("      <text x=\"").Append(Number(node.X + 34)).Append("\" y=\"").Append(Number(textY))
                    .Append("\" font-size=\"10\" fill=\"").Append(TextColor).Append("\">")
                    .Append(EscapeXml(field.ApiName)).AppendLine("</text>");
            }

            builder.AppendLine("    </g>");
        }

        builder.AppendLine("  </g>");
        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    // ------------------------------------------------------------- raster

    private static byte[] RenderRaster(ErdDocument document, ErdImageOptions options, SKEncodedImageFormat format, int quality)
    {
        var layout = BuildLayout(document, options);
        var scale = Math.Max(1, options.Scale);
        var width = Math.Max(1, layout.Width * scale);
        var height = Math.Max(1, layout.Height * scale);

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(scale);
        canvas.Clear(SKColors.White);

        using var typeface = CreateTypeface();
        using var border = new SKPaint { Color = SKColor.Parse(BorderColor), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f };
        using var headerFill = new SKPaint { Color = SKColor.Parse(HeaderColor), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var text = new SKPaint { Color = SKColor.Parse(TextColor), IsAntialias = true, Typeface = typeface, TextSize = 10 };
        using var titleText = new SKPaint { Color = SKColor.Parse(TextColor), IsAntialias = true, Typeface = typeface, TextSize = 13, FakeBoldText = true };
        using var keyText = new SKPaint { Color = SKColor.Parse(ForeignKeyColor), IsAntialias = true, Typeface = typeface, TextSize = 9 };
        using var line = new SKPaint { Color = SKColor.Parse(BorderColor), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f };

        foreach (var edge in layout.Edges)
        {
            using var path = new SKPath();
            path.MoveTo(edge.Points[0].X, edge.Points[0].Y);
            foreach (var point in edge.Points.Skip(1))
            {
                path.LineTo(point.X, point.Y);
            }

            canvas.DrawPath(path, line);
            var last = edge.Points[^1];
            canvas.DrawText(edge.Relationship.FieldApiName, last.X + 6, last.Y - 4, keyText);
        }

        foreach (var node in layout.Nodes)
        {
            var rect = new SKRect(node.X, node.Y, node.X + node.Width, node.Y + node.Height);
            canvas.DrawRoundRect(rect, 8, 8, white);
            var headerRect = new SKRect(node.X, node.Y, node.X + node.Width, node.Y + HeaderHeight);
            canvas.DrawRoundRect(headerRect, 8, 8, headerFill);
            canvas.DrawRect(new SKRect(node.X, node.Y + HeaderHeight - 8, node.X + node.Width, node.Y + HeaderHeight), headerFill);
            canvas.DrawRoundRect(rect, 8, 8, border);
            canvas.DrawText(Trim(node.Entity.DisplayName(document.NameStyle), titleText, node.Width - 20), node.X + 10, node.Y + 22, titleText);

            for (var i = 0; i < node.Fields.Count; i++)
            {
                var field = node.Fields[i];
                var textY = node.Y + HeaderHeight + 6 + ((i + 1) * RowHeight) - 4;
                if (field.IsPrimaryKey || field.IsLookup)
                {
                    using var paint = new SKPaint { Color = field.IsPrimaryKey ? SKColor.Parse(BorderColor) : SKColor.Parse(ForeignKeyColor), IsAntialias = true, Typeface = typeface, TextSize = 9 };
                    canvas.DrawText(field.IsPrimaryKey ? "PK" : "FK", node.X + 10, textY, paint);
                }

                canvas.DrawText(Trim(field.ApiName, text, node.Width - 45), node.X + 34, textY, text);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
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

    // ------------------------------------------------------------ helpers

    private static string Trim(string value, SKPaint paint, float maxWidth)
    {
        if (paint.MeasureText(value) <= maxWidth)
        {
            return value;
        }

        var text = value;
        while (text.Length > 1 && paint.MeasureText(text + "…") > maxWidth)
        {
            text = text[..^1];
        }

        return text + "…";
    }

    private static string Format(IReadOnlyList<(float X, float Y)> points)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(" L ");
            }

            builder.Append(Number(points[i].X)).Append(' ').Append(Number(points[i].Y));
        }

        return builder.ToString();
    }

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
