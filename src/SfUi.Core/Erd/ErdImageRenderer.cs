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

    /// <summary>1 リングあたりの配置数（0 = 自動: 円周から算出）。</summary>
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

    /// <summary>レイアウト計算中に使うノード（中心座標を持つ）。</summary>
    private sealed class LayoutNode
    {
        public required ErdEntity Entity { get; init; }

        public required IReadOnlyList<ErdField> Fields { get; init; }

        public required float Width { get; init; }

        public required float Height { get; init; }

        public float CenterX { get; set; }

        public float CenterY { get; set; }
    }

    /// <summary>
    /// 関連の多いエンティティを中心に置き、残りを同心円状（放射状）に配置する。
    /// 次数の高い順に外側のリングへ並べ、既に配置済みの関連先の方向へ寄せて配置するため、
    /// 関連するオブジェクト同士が近くに集まりやすい。
    /// </summary>
    public static ErdImageLayout BuildLayout(ErdDocument document, ErdImageOptions? options = null)
    {
        options ??= new ErdImageOptions();
        var entities = document.Entities;

        // 次数（関連数）: 表示対象どうしの関係だけを数える
        var known = entities.Select(entity => entity.ApiName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var degree = known.ToDictionary(name => name, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in document.Relationships)
        {
            if (!known.Contains(relationship.ChildApiName) || !known.Contains(relationship.ParentApiName))
            {
                continue;
            }

            degree[relationship.ParentApiName]++;
            if (!string.Equals(relationship.ChildApiName, relationship.ParentApiName, StringComparison.OrdinalIgnoreCase))
            {
                degree[relationship.ChildApiName]++;
            }
        }

        var prepared = entities
            .Select(entity =>
            {
                var fields = PickFields(entity, options);
                return new LayoutNode
                {
                    Entity = entity,
                    Fields = fields,
                    Width = options.NodeWidth,
                    Height = HeaderHeight + (fields.Count * RowHeight) + (Padding * 2),
                };
            })
            .OrderByDescending(node => degree[node.Entity.ApiName])
            .ThenBy(node => node.Entity.ApiName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PlaceRadially(prepared, document.Relationships, options);

        // 中心座標 → 左上座標へ変換し、余白を付けて正規化する
        var minX = prepared.Count == 0 ? 0 : prepared.Min(node => node.CenterX - (node.Width / 2));
        var minY = prepared.Count == 0 ? 0 : prepared.Min(node => node.CenterY - (node.Height / 2));
        var nodes = prepared
            .Select(node => new ErdImageLayout.ErdNode
            {
                Entity = node.Entity,
                Fields = node.Fields,
                X = node.CenterX - (node.Width / 2) - minX + Margin,
                Y = node.CenterY - (node.Height / 2) - minY + Margin,
                Width = node.Width,
                Height = node.Height,
            })
            .ToList();

        var byName = nodes.ToDictionary(node => node.Entity.ApiName, StringComparer.OrdinalIgnoreCase);
        var edges = new List<ErdImageLayout.ErdEdge>();
        var laneOffsets = new[] { 0f, 14f, -14f, 26f, -26f, 7f, -7f };
        var edgeIndex = 0;
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
                Points = RouteEdge(parent, child, laneOffsets[edgeIndex % laneOffsets.Length]),
            });
            edgeIndex++;
        }

        var totalWidth = (int)Math.Ceiling(nodes.Count == 0 ? 600 : nodes.Max(node => node.X + node.Width) + Margin + 70);
        var totalHeight = (int)Math.Ceiling(nodes.Count == 0 ? 300 : nodes.Max(node => node.Y + node.Height) + Margin + 40);
        return new ErdImageLayout { Nodes = nodes, Edges = edges, Width = totalWidth, Height = totalHeight };
    }

    /// <summary>表示する項目（PK → 参照 → 必須の順で最大 MaxFieldsPerNode 件）。</summary>
    private static IReadOnlyList<ErdField> PickFields(ErdEntity entity, ErdImageOptions options) =>
        entity.Fields
            .OrderByDescending(field => field.IsPrimaryKey)
            .ThenByDescending(field => field.IsLookup)
            .ThenBy(field => field.ApiName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, options.MaxFieldsPerNode))
            .ToList();

    /// <summary>
    /// 中心 1 件 + 同心円リングへ配置する。リングは内側から順に置き、
    /// ノード同士が重なる場合はそのリングの半径を外へ広げる（放射状の見た目を保ったまま重なりを解消）。
    /// </summary>
    private static void PlaceRadially(List<LayoutNode> nodes, IReadOnlyList<ErdRelationship> relationships, ErdImageOptions options)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        var lookup = new Dictionary<string, LayoutNode>(StringComparer.OrdinalIgnoreCase);
        var placed = new List<LayoutNode>();
        var center = nodes[0];
        center.CenterX = 0;
        center.CenterY = 0;
        lookup[center.Entity.ApiName] = center;
        placed.Add(center);

        var radius = (options.NodeWidth * 0.5f) + GapX;
        var index = 1;
        while (index < nodes.Count)
        {
            var capacity = options.Columns > 0
                ? Math.Max(1, options.Columns)
                : Math.Max(2, (int)Math.Floor((2 * Math.PI * radius) / (options.NodeWidth + (GapX * 0.35f))));
            var count = Math.Min(capacity, nodes.Count - index);
            var ring = new List<LayoutNode>(count);
            var slots = new List<int>(count);
            var used = new bool[capacity];
            for (var i = 0; i < count; i++)
            {
                var node = nodes[index + i];
                var slot = PickSlot(node, used, capacity, relationships, lookup);
                used[slot] = true;
                ring.Add(node);
                slots.Add(slot);
            }

            void ApplyRing()
            {
                for (var i = 0; i < ring.Count; i++)
                {
                    var angle = (-Math.PI / 2) + ((2 * Math.PI * slots[i]) / capacity);
                    ring[i].CenterX = (float)(radius * Math.Cos(angle));
                    ring[i].CenterY = (float)(radius * Math.Sin(angle));
                }
            }

            ApplyRing();
            for (var guard = 0; HasOverlap(ring, placed) && guard < 40; guard++)
            {
                radius *= 1.08f;
                ApplyRing();
            }

            foreach (var node in ring)
            {
                placed.Add(node);
                lookup[node.Entity.ApiName] = node;
            }

            index += count;
            radius += options.NodeWidth + (GapX * 0.35f);
        }
    }

    private static bool HasOverlap(IReadOnlyList<LayoutNode> ring, IReadOnlyList<LayoutNode> placed)
    {
        const float gap = 26f;
        foreach (var node in ring)
        {
            foreach (var other in placed)
            {
                if (Overlaps(node, other, gap))
                {
                    return true;
                }
            }
        }

        for (var i = 0; i < ring.Count; i++)
        {
            for (var j = i + 1; j < ring.Count; j++)
            {
                if (Overlaps(ring[i], ring[j], gap))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Overlaps(LayoutNode a, LayoutNode b, float gap) =>
        Math.Abs(a.CenterX - b.CenterX) < ((a.Width + b.Width) / 2) + gap &&
        Math.Abs(a.CenterY - b.CenterY) < ((a.Height + b.Height) / 2) + gap;

    /// <summary>配置済みの関連先がある方角に最も近い空きスロットを選ぶ。</summary>
    private static int PickSlot(
        LayoutNode node,
        bool[] used,
        int capacity,
        IReadOnlyList<ErdRelationship> relationships,
        IReadOnlyDictionary<string, LayoutNode> placed)
    {
        float sumX = 0;
        float sumY = 0;
        var neighbors = 0;
        foreach (var relationship in relationships)
        {
            LayoutNode? other = null;
            if (string.Equals(relationship.ChildApiName, node.Entity.ApiName, StringComparison.OrdinalIgnoreCase))
            {
                placed.TryGetValue(relationship.ParentApiName, out other);
            }
            else if (string.Equals(relationship.ParentApiName, node.Entity.ApiName, StringComparison.OrdinalIgnoreCase))
            {
                placed.TryGetValue(relationship.ChildApiName, out other);
            }

            if (other is null || ReferenceEquals(other, node))
            {
                continue;
            }

            sumX += other.CenterX;
            sumY += other.CenterY;
            neighbors++;
        }

        var best = -1;
        if (neighbors > 0)
        {
            var desired = Math.Atan2(sumY, sumX);
            var bestDistance = double.MaxValue;
            for (var i = 0; i < capacity; i++)
            {
                if (used[i])
                {
                    continue;
                }

                var angle = (-Math.PI / 2) + ((2 * Math.PI * i) / capacity);
                var distance = Math.Abs(AngleDelta(angle, desired));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
        }

        if (best >= 0)
        {
            return best;
        }

        for (var i = 0; i < capacity; i++)
        {
            if (!used[i])
            {
                return i;
            }
        }

        return 0;
    }

    private static double AngleDelta(double angle, double target)
    {
        var delta = angle - target;
        while (delta > Math.PI)
        {
            delta -= 2 * Math.PI;
        }

        while (delta < -Math.PI)
        {
            delta += 2 * Math.PI;
        }

        return delta;
    }

    private static List<(float X, float Y)> RouteEdge(ErdImageLayout.ErdNode parent, ErdImageLayout.ErdNode child, float lane = 0)
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

        // 放射状レイアウト: 親 → 子の中心を結ぶ向きで矩形の境界点を求め、その間に折れ点を置く。
        // lane は垂直方向のずれ（同じ向きの線を扇状に分ける）。
        var centerX = parent.X + (parent.Width / 2);
        var centerY = parent.Y + (parent.Height / 2);
        var targetX = child.X + (child.Width / 2);
        var targetY = child.Y + (child.Height / 2);
        var deltaX = targetX - centerX;
        var deltaY = targetY - centerY;
        var length = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length < 1f)
        {
            points.Add((centerX, centerY));
            points.Add((targetX, targetY));
            return points;
        }

        var unitX = deltaX / length;
        var unitY = deltaY / length;
        var normalX = -unitY;
        var normalY = unitX;

        var start = BoundaryPoint(parent, unitX, unitY);
        var end = BoundaryPoint(child, -unitX, -unitY);

        // 始点・終点も少しずらして線を扇状に広げる（境界上で重ならないように）
        var startShift = lane * 0.35f;
        var endShift = lane * 0.35f;
        var startX = start.X + (normalX * startShift);
        var startY = start.Y + (normalY * startShift);
        var endX = end.X + (normalX * endShift);
        var endY = end.Y + (normalY * endShift);
        var bend = lane - ((startShift + endShift) / 2);
        var midX = ((startX + endX) / 2) + (normalX * bend);
        var midY = ((startY + endY) / 2) + (normalY * bend);

        points.Add((startX, startY));
        points.Add((midX, midY));
        points.Add((endX, endY));
        return points;
    }

    /// <summary>ノード中心から (unitX, unitY) 方向へ進んだときに矩形と交わる点。</summary>
    private static (float X, float Y) BoundaryPoint(ErdImageLayout.ErdNode node, float unitX, float unitY)
    {
        var halfWidth = node.Width / 2;
        var halfHeight = node.Height / 2;
        var centerX = node.X + halfWidth;
        var centerY = node.Y + halfHeight;
        if (MathF.Abs(unitX) < 1e-4f)
        {
            return (centerX, centerY + (MathF.Sign(unitY) * halfHeight));
        }

        if (MathF.Abs(unitY) < 1e-4f)
        {
            return (centerX + (MathF.Sign(unitX) * halfWidth), centerY);
        }

        var distance = MathF.Min(halfWidth / MathF.Abs(unitX), halfHeight / MathF.Abs(unitY));
        return (centerX + (unitX * distance), centerY + (unitY * distance));
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

        // 鳥の足（子側の終端）。線種別ごとに色を合わせたマーカーを 3 つ定義する。
        builder.AppendLine("  <defs>");
        AppendCrowMarker(builder, "crow-detail", "#1F3A5F", "1.8");
        AppendCrowMarker(builder, "crow-required", BorderColor, "1.4");
        AppendCrowMarker(builder, "crow-optional", "#8A94A6", "1.2");
        builder.AppendLine("  </defs>");

        builder.AppendLine("  <g id=\"edges\" fill=\"none\">");
        var showLabels = layout.Edges.Count <= 12;
        var showCardinality = layout.Edges.Count <= 40;
        foreach (var edge in layout.Edges)
        {
            // 主従 = 太い実線、必須参照 = 実線、任意参照 = 破線で描き分ける
            var isDetail = edge.Relationship.IsMasterDetail;
            var isRequired = edge.Relationship.IsRequired;
            var stroke = isDetail ? "#1F3A5F" : isRequired ? BorderColor : "#8A94A6";
            var width = isDetail ? "2.4" : isRequired ? "1.8" : "1.4";
            var dash = isDetail || isRequired ? string.Empty : " stroke-dasharray=\"5 3\"";
            var marker = isDetail ? "crow-detail" : isRequired ? "crow-required" : "crow-optional";
            builder.Append("    <path d=\"M ").Append(Format(edge.Points));
            builder.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(width)
                .Append('"').Append(dash).Append(" marker-end=\"url(#").Append(marker).Append(")\" />").AppendLine();

            // 親側 = 1、子側 = N（放射状レイアウトでも読めるよう、線の向きに沿って置く）
            if (showCardinality)
            {
                var first = edge.Points[0];
                var next = edge.Points.Count > 1 ? edge.Points[1] : first;
                var last = edge.Points[^1];
                var previous = edge.Points.Count > 1 ? edge.Points[^2] : last;
                var startUnit = Unit(next.X - first.X, next.Y - first.Y);
                var endUnit = Unit(last.X - previous.X, last.Y - previous.Y);
                var one = MarkerPoint(first, startUnit, 12f);
                var n = MarkerPoint(last, endUnit, -12f);
                builder.Append("    <text x=\"").Append(Number(one.X)).Append("\" y=\"").Append(Number(one.Y))
                    .Append("\" font-size=\"10\" font-weight=\"600\" text-anchor=\"middle\" fill=\"").Append(stroke).AppendLine("\">1</text>");
                builder.Append("    <text x=\"").Append(Number(n.X)).Append("\" y=\"").Append(Number(n.Y))
                    .Append("\" font-size=\"10\" font-weight=\"600\" text-anchor=\"middle\" fill=\"").Append(stroke).AppendLine("\">N</text>");
            }

            if (!showLabels)
            {
                continue;
            }

            var label = LabelPoint(edge.Points);
            builder.Append("    <text x=\"").Append(Number(label.X)).Append("\" y=\"").Append(Number(label.Y))
                .Append("\" font-size=\"9\" text-anchor=\"middle\" fill=\"").Append(SubTextColor)
                .Append("\" stroke=\"#FFFFFF\" stroke-width=\"3\" paint-order=\"stroke\">")
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

        var showLabels = layout.Edges.Count <= 12;
        var showCardinality = layout.Edges.Count <= 40;
        foreach (var edge in layout.Edges)
        {
            var isDetail = edge.Relationship.IsMasterDetail;
            var isRequired = edge.Relationship.IsRequired;
            using var edgePaint = new SKPaint
            {
                Color = SKColor.Parse(isDetail ? "#1F3A5F" : isRequired ? BorderColor : "#8A94A6"),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isDetail ? 2.4f : isRequired ? 1.8f : 1.4f,
            };
            if (!isDetail && !isRequired)
            {
                edgePaint.PathEffect = SKPathEffect.CreateDash(new[] { 5f, 3f }, 0);
            }

            using var path = new SKPath();
            path.MoveTo(edge.Points[0].X, edge.Points[0].Y);
            foreach (var point in edge.Points.Skip(1))
            {
                path.LineTo(point.X, point.Y);
            }

            canvas.DrawPath(path, edgePaint);

            // 親側 = 1、子側 = N（密な図では省略）
            if (showCardinality)
            {
                using var markerPaint = new SKPaint
                {
                    Color = edgePaint.Color,
                    IsAntialias = true,
                    Typeface = typeface,
                    TextSize = 10,
                    FakeBoldText = true,
                };
                var first = edge.Points[0];
                var next = edge.Points.Count > 1 ? edge.Points[1] : first;
                var last = edge.Points[^1];
                var previous = edge.Points.Count > 1 ? edge.Points[^2] : last;
                var startUnit = Unit(next.X - first.X, next.Y - first.Y);
                var endUnit = Unit(last.X - previous.X, last.Y - previous.Y);
                var one = MarkerPoint(first, startUnit, 12f);
                var n = MarkerPoint(last, endUnit, -12f);
                canvas.DrawText("1", one.X - (markerPaint.MeasureText("1") / 2), one.Y, markerPaint);
                canvas.DrawText("N", n.X - (markerPaint.MeasureText("N") / 2), n.Y, markerPaint);
            }

            if (!showLabels)
            {
                continue;
            }

            var label = LabelPoint(edge.Points);
            var textWidth = keyText.MeasureText(edge.Relationship.FieldApiName);
            using var halo = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                Typeface = typeface,
                TextSize = 9,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3,
            };
            canvas.DrawText(edge.Relationship.FieldApiName, label.X - (textWidth / 2), label.Y, halo);
            canvas.DrawText(edge.Relationship.FieldApiName, label.X - (textWidth / 2), label.Y, keyText);
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

    /// <summary>エッジラベルを置く位置（曲がり角の少し上）。</summary>
    private static (float X, float Y) LabelPoint(IReadOnlyList<(float X, float Y)> points)
    {
        if (points.Count == 3)
        {
            return (points[1].X, points[1].Y - 2);
        }

        if (points.Count >= 4)
        {
            var a = points[1];
            var b = points[2];
            return ((a.X + b.X) / 2, ((a.Y + b.Y) / 2) - 2);
        }

        var first = points[0];
        var last = points[^1];
        return ((first.X + last.X) / 2, ((first.Y + last.Y) / 2) - 2);
    }

    private static (float X, float Y) Unit(float deltaX, float deltaY)
    {
        var length = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        return length < 1e-4f ? (0f, -1f) : (deltaX / length, deltaY / length);
    }

    /// <summary>線の向きに沿って distance 進め、垂直方向へ少しずらしたラベル位置（1 / N 用）。</summary>
    private static (float X, float Y) MarkerPoint((float X, float Y) point, (float X, float Y) unit, float distance)
    {
        var x = point.X + (unit.X * distance) + (-unit.Y * 7);
        var y = point.Y + (unit.Y * distance) + (unit.X * 7) + 4;
        return (x, y);
    }

    /// <summary>鳥の足マーカー（子側の終端）を 1 つ出力する。</summary>
    private static void AppendCrowMarker(StringBuilder builder, string id, string stroke, string width)
    {
        builder.Append("    <marker id=\"").Append(id)
            .Append("\" viewBox=\"0 0 10 10\" refX=\"8\" refY=\"5\" markerWidth=\"9\" markerHeight=\"9\" orient=\"auto\">")
            .Append("<path d=\"M 1 1 L 8 5 L 1 9\" fill=\"none\" stroke=\"").Append(stroke)
            .Append("\" stroke-width=\"").Append(width).AppendLine("\" />");
        builder.AppendLine("    </marker>");
    }

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
