using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SfUi.Avalonia.Converters;

/// <summary>使用率の警告状態をバーの色へ変換する（80% 以上 = 赤 / 通常 = 緑）。</summary>
public sealed class WarningBrushConverter : IValueConverter
{
    private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#C62828"));
    private static readonly IBrush Normal = new SolidColorBrush(Color.Parse("#2E7D32"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Warning : Normal;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
