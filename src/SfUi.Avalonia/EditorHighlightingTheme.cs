using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace SfUi.Avalonia;

/// <summary>
/// エディタの構文ハイライトをダーク テーマ向けに変換します（WPF 版と同じロジック）。
/// </summary>
public static class EditorHighlightingTheme
{
    /// <summary>テーマに合わせた定義を返す（ライトはそのまま）。</summary>
    public static IHighlightingDefinition Resolve(IHighlightingDefinition definition, bool dark)
        => dark ? new DarkDefinition(definition) : definition;

    /// <summary>XML 文字列から読み込む（SOQL など自前の定義用）。</summary>
    public static IHighlightingDefinition LoadFromXshd(string xshd)
        => HighlightingLoader.Load(
            System.Xml.XmlReader.Create(new System.IO.StringReader(xshd)),
            HighlightingManager.Instance);

    /// <summary>明るい背景の色を暗い背景で読める色へ変換する（色相は保つ）。</summary>
    internal static Color Lift(Color color)
    {
        var (h, s, l) = ToHsl(color);
        if (l >= 0.55)
        {
            return color;
        }

        var lifted = FromHsl(h, Math.Min(1.0, s * 0.9), Math.Min(0.86, 0.62 + (l * 0.25)));
        return Color.FromRgb(lifted.R, lifted.G, lifted.B);
    }

    private static (double H, double S, double L) ToHsl(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (Math.Abs(max - min) < 0.0001)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (Math.Abs(max - r) < 0.0001)
        {
            h = ((g - b) / d) + (g < b ? 6 : 0);
        }
        else if (Math.Abs(max - g) < 0.0001)
        {
            h = ((b - r) / d) + 2;
        }
        else
        {
            h = ((r - g) / d) + 4;
        }

        return (h / 6, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        if (s <= 0)
        {
            var v = (byte)Math.Round(l * 255);
            return Color.FromRgb(v, v, v);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;
        return Color.FromRgb(Channel(p, q, h + (1.0 / 3)), Channel(p, q, h), Channel(p, q, h - (1.0 / 3)));
    }

    private static byte Channel(double p, double q, double t)
    {
        if (t < 0) { t += 1; }
        if (t > 1) { t -= 1; }
        if (t < 1.0 / 6) { return (byte)Math.Round((p + ((q - p) * 6 * t)) * 255); }
        if (t < 1.0 / 2) { return (byte)Math.Round(q * 255); }
        if (t < 2.0 / 3) { return (byte)Math.Round((p + ((q - p) * ((2.0 / 3) - t) * 6)) * 255); }
        return (byte)Math.Round(p * 255);
    }

    private sealed class DarkDefinition : IHighlightingDefinition
    {
        private readonly IHighlightingDefinition _inner;
        private readonly Dictionary<string, HighlightingColor> _colors;

        public DarkDefinition(IHighlightingDefinition inner)
        {
            _inner = inner;
            _colors = new Dictionary<string, HighlightingColor>(StringComparer.Ordinal);
            foreach (var color in inner.NamedHighlightingColors)
            {
                var name = color.Name ?? string.Empty;
                _colors[name] = Convert(color);
            }
        }

        public string Name => _inner.Name;

        public HighlightingRuleSet MainRuleSet => _inner.MainRuleSet;

        public IEnumerable<HighlightingColor> NamedHighlightingColors => _colors.Values;

        public IDictionary<string, string> Properties => _inner.Properties;

        public HighlightingColor? GetNamedColor(string name)
            => _colors.TryGetValue(name, out var color) ? color : _inner.GetNamedColor(name);

        public HighlightingRuleSet? GetNamedRuleSet(string name) => _inner.GetNamedRuleSet(name);

        private static HighlightingColor Convert(HighlightingColor source) => new()
        {
            Name = source.Name,
            FontWeight = source.FontWeight,
            FontStyle = source.FontStyle,
            Underline = source.Underline,
            Foreground = Lift(source.Foreground),
            Background = source.Background,
        };

        private static HighlightingBrush? Lift(HighlightingBrush? brush)
        {
            if (brush is null)
            {
                return null;
            }

            var color = brush.GetColor(null!);
            return color is null ? brush : new SimpleHighlightingBrush(EditorHighlightingTheme.Lift(color.Value));
        }
    }
}
