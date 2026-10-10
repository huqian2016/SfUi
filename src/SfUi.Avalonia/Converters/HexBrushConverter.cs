using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SfUi.Avalonia.Converters;

/// <summary>#RRGGBB 文字列を IBrush へ変換する（フロー グラフ用）。</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string hex && hex.StartsWith('#') && Color.TryParse(hex, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
