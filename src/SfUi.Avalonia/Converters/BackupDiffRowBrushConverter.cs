using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SfUi.App.ViewModels;

namespace SfUi.Avalonia.Converters;

/// <summary>バックアップ比較のレコード差分行の背景色（追加=緑 / 削除=赤。両方なら削除を優先）。</summary>
public sealed class BackupDiffRowBrushConverter : IValueConverter
{
    private static readonly IBrush Added = new SolidColorBrush(Color.Parse("#E6F4EA"));
    private static readonly IBrush Removed = new SolidColorBrush(Color.Parse("#FCE8E6"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not BackupCompareRecordRowViewModel row)
        {
            return AvaloniaProperty.UnsetValue;
        }

        if (row.IsRemoved)
        {
            return Removed;
        }

        return row.IsAdded ? Added : AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
