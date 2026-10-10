using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SfUi.App.Views;

/// <summary>#RRGGBB 文字列を Brush へ変換する（フロー グラフ用）。</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && hex.StartsWith('#') && hex.Length is 7 or 9)
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch
            {
                // 不正な色指定は透明で描画する
            }
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
