using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Converters;

/// <summary>
/// 比較セルの状態フラグを表示スタイルへ変換する。
/// parameter "foreground" → 文字色（失敗=赤 / 欠落・未取得=グレー）、"italic" → 斜体。
/// </summary>
public sealed class CompareCellConverter : IValueConverter
{
    private static readonly IBrush Gray = new SolidColorBrush(Color.Parse("#808080"));
    private static readonly IBrush Failed = new SolidColorBrush(Color.Parse("#B00020"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CompareCellViewModel cell)
        {
            return AvaloniaProperty.UnsetValue;
        }

        if ((parameter as string) == "italic")
        {
            return cell.IsMissing || cell.IsNotFetched || cell.IsFailed
                ? FontStyle.Italic
                : AvaloniaProperty.UnsetValue;
        }

        if (cell.IsFailed)
        {
            return Failed;
        }

        return cell.IsMissing || cell.IsNotFetched ? Gray : AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>差分のある行の背景色（true → 黄色）。</summary>
public sealed class DiffRowBrushConverter : IValueConverter
{
    private static readonly IBrush Diff = new SolidColorBrush(Color.Parse("#FFF59D"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Diff : AvaloniaProperty.UnsetValue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
