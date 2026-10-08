using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SfUi.Core;

namespace SfUi.App.Views;

/// <summary>
/// 上部バー用のヘルプ アイコン ボタン。クリックすると、現在の UI 言語とウィンドウ（<see cref="Topic"/>）に
/// 対応するユーザーマニュアル（GitHub）の章を既定のブラウザーで開く。
/// </summary>
public class HelpButton : Button
{
    public HelpButton()
    {
        Width = 32;
        Height = 26;
        Padding = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        Content = new Path
        {
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            Fill = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            Data = Application.Current?.TryFindResource("IconBookQuestion") as Geometry,
        };

        UpdateHelpText();
        UiText.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => UiText.LanguageChanged -= OnLanguageChanged;
        Click += (_, _) => ManualLinks.TryOpen(Topic);
    }

    /// <summary>このボタンが開くマニュアルの章。</summary>
    public ManualLinks.Topic Topic { get; set; } = ManualLinks.Topic.MainWindow;

    private void OnLanguageChanged() => UpdateHelpText();

    private void UpdateHelpText()
    {
        var text = UiText.T("Help_OpenManual");
        ToolTip = text;
        AutomationProperties.SetName(this, text);
    }
}
