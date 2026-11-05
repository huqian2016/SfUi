using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SfUi.Core;

namespace SfUi.App;

/// <summary>
/// Windows のタイトル バー（非クライアント領域）をダーク/ライトに切り替えます。
/// DWM の <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c>（Windows 10 1809 以降）を使うため、
/// ウィンドウのスタイルを自前で描画しなくても枠の色だけが変わります。
/// </summary>
internal static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>指定ウィンドウのタイトル バーをテーマに合わせる。</summary>
    public static void Apply(Window window, bool dark)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return; // まだ HWND が無い（表示前）
        }

        var value = dark ? 1 : 0;
        var result = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
        if (result != 0)
        {
            // Windows 10 1809〜1909 は属性 ID 19
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int));
        }
    }

    /// <summary>現在のテーマを、開いている全ウィンドウと今後開くウィンドウへ適用する。</summary>
    public static void ApplyToAllWindows()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var dark = AppTheme.IsDark;
        foreach (Window window in app.Windows)
        {
            Apply(window, dark);
        }
    }
}
