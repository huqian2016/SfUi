using System.Globalization;
using Avalonia.Data.Converters;

namespace SfUi.Avalonia.Converters;

/// <summary>数値が 0 のとき true（空メッセージの表示制御用）。</summary>
public sealed class IsZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
