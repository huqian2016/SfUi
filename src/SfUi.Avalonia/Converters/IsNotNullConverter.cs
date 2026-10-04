using System.Globalization;
using Avalonia.Data.Converters;

namespace SfUi.Avalonia.Converters;

/// <summary>値が null でないとき true（空メッセージの表示制御用）。</summary>
public sealed class IsNotNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
