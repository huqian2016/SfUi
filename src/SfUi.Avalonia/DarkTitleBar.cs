using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SfUi.Core;

namespace SfUi.Avalonia;

/// <summary>
/// Windows のタイトル バーをダーク/ライトに切り替えます（WPF 版と同じ DWM 属性を使用）。
/// macOS / Linux では何もしません。
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

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var value = dark ? 1 : 0;
        var result = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
        if (result != 0)
        {
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int));
        }
    }

    /// <summary>現在のテーマを、開いている全ウィンドウへ適用する。</summary>
    public static void ApplyToAllWindows()
    {
        if (Application.Current?.ApplicationLifetime is not ClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var dark = AppTheme.IsDark;
        foreach (var window in desktop.Windows)
        {
            Apply(window, dark);
        }
    }
}

/// <summary>
/// すべてのウィンドウにタイトル バーのテーマ適用を仕込むための添付プロパティ。
/// App.axaml の <c>&lt;Style Selector="Window"&gt;</c> から全ウィンドウに設定されます。
/// </summary>
public sealed class DarkTitleBarBehavior : AvaloniaObject
{
    /// <summary>true にすると、そのウィンドウのタイトル バーがテーマに追従します。</summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DarkTitleBarBehavior, Window, bool>("Enabled");

    static DarkTitleBarBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<Window>((window, e) =>
        {
            if (e.NewValue is true)
            {
                window.Opened += (_, _) => DarkTitleBar.Apply(window, AppTheme.IsDark);
                DarkTitleBar.Apply(window, AppTheme.IsDark);
            }
        });
    }

    /// <summary>添付プロパティの取得。</summary>
    public static bool GetEnabled(Window window) => window.GetValue(EnabledProperty);

    /// <summary>添付プロパティの設定。</summary>
    public static void SetEnabled(Window window, bool value) => window.SetValue(EnabledProperty, value);
}
