using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Converters;

/// <summary>バックアップ比較行の背景色（差分 / エラー / A のみ / B のみ）。</summary>
public sealed class BackupCompareRowBrushConverter : IValueConverter
{
    private static readonly IBrush OnlyInB = new SolidColorBrush(Color.Parse("#F3E5F5"));
    private static readonly IBrush OnlyInA = new SolidColorBrush(Color.Parse("#E8EAF6"));
    private static readonly IBrush HasError = new SolidColorBrush(Color.Parse("#FDECEA"));
    private static readonly IBrush HasDiff = new SolidColorBrush(Color.Parse("#FFF3CD"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not BackupCompareObjectRowViewModel row)
        {
            return AvaloniaProperty.UnsetValue;
        }

        if (row.OnlyInB)
        {
            return OnlyInB;
        }

        if (row.OnlyInA)
        {
            return OnlyInA;
        }

        if (row.HasError)
        {
            return HasError;
        }

        return row.HasDiff ? HasDiff : AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
