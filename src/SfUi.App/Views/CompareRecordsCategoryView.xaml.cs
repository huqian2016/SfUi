using System.Windows;
using System.Windows.Controls;

namespace SfUi.App.Views;

/// <summary>「レコード比較」カテゴリ（ツールバー + 比較グリッド）。</summary>
public partial class CompareRecordsCategoryView : UserControl
{
    public CompareRecordsCategoryView()
    {
        InitializeComponent();
    }

    /// <summary>「比較実行」で比較項目の選択パネルを畳む（結果が見えるように）。</summary>
    private void OnRunClick(object sender, RoutedEventArgs e) => FieldsExpander.IsExpanded = false;
}
