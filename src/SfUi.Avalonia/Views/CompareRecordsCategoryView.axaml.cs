using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SfUi.Avalonia.Views;

/// <summary>「レコード比較」カテゴリ（ツールバー + 比較グリッド）。</summary>
public partial class CompareRecordsCategoryView : UserControl
{
    public CompareRecordsCategoryView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>「比較実行」で比較項目の選択パネルを畳む（結果が見えるように）。</summary>
    private void OnRunClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<Expander>("FieldsExpander")?.SetCurrentValue(Expander.IsExpandedProperty, false);
    }
}
