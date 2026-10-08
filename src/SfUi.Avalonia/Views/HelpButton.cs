using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SfUi.Core;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace SfUi.Avalonia.Views;

/// <summary>
/// 上部バー用のヘルプ アイコン ボタン。クリックすると、現在の UI 言語とウィンドウ（<see cref="Topic"/>）に
/// 対応するユーザーマニュアル（GitHub）の章を既定のブラウザーで開く（WPF 版と同一挙動）。
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
        Content = new ShapePath
        {
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            Fill = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            Data = Application.Current?.TryFindResource("IconBookQuestion", out var resource) == true
                ? resource as Geometry
                : null,
        };

        UpdateHelpText();
        UiText.LanguageChanged += OnLanguageChanged;
        DetachedFromVisualTree += (_, _) => UiText.LanguageChanged -= OnLanguageChanged;
        Click += (_, _) => ManualLinks.TryOpen(Topic);
    }

    /// <summary>このボタンが開くマニュアルの章。</summary>
    public ManualLinks.Topic Topic { get; set; } = ManualLinks.Topic.MainWindow;

    private void OnLanguageChanged() => UpdateHelpText();

    private void UpdateHelpText()
    {
        var text = UiText.T("Help_OpenManual");
        ToolTip.SetTip(this, text);
        AutomationProperties.SetName(this, text);
    }
}
