using System.Windows;

namespace SfUi.App.Services;

/// <summary>
/// 子ウィンドウの位置決めヘルパー。
/// 所有（Owner）ウィンドウは Windows の仕様で常に親より前面に固定されるため、非モーダルの子ウィンドウには
/// Owner を設定しない（メインウィンドウを前に出せるようにする）。その代わり、開いたときの中央配置を自前で行う。
/// </summary>
public static class WindowPlacement
{
    /// <summary>基準ウィンドウ（通常はメインウィンドウ）の中央に配置する。基準が null・サイズ未指定のときは既定位置。</summary>
    public static void CenterOn(Window window, Window? reference)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        if (reference is null || double.IsNaN(window.Width) || double.IsNaN(window.Height))
        {
            return;
        }

        window.Left = reference.Left + ((reference.ActualWidth - window.Width) / 2);
        window.Top = reference.Top + ((reference.ActualHeight - window.Height) / 2);
    }
}
