using System.Globalization;
using System.Windows.Data;

namespace SfUi.App.Services;

/// <summary>
/// 文字列（列挙キー）とラジオボタンのチェック状態を相互変換する。
/// チェックされた側だけが値を書き戻し、外れた側は何もしない（Binding.DoNothing）。
/// </summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? parameter.ToString()! : Binding.DoNothing;
}
