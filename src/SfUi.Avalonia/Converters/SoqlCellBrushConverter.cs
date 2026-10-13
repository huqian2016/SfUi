using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SfUi.Avalonia.Converters;

/// <summary>
/// SOQL 結果グリッドのセル状態（<see cref="SfUi.App.ViewModels.SoqlCell.StyleState"/>）を背景色へ変換する。
/// 0 = 変更なし（透明）/ 1 = 組織へ未反映（黄）/ 2 = 反映エラー（赤）。
/// </summary>
public sealed class SoqlCellBrushConverter : IValueConverter
{
    private static readonly IBrush Dirty = new SolidColorBrush(Color.Parse("#FFF3CD"));
    private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#F8D7DA"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        1 => Dirty,
        2 => Error,
        _ => Brushes.Transparent,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
