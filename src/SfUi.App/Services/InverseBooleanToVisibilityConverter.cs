using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SfUi.App.Services;

/// <summary>true のときに Collapsed、false のときに Visible にする（WPF に組み込みの反転版が無いため）。</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}
