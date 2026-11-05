using System;
using System.Windows;
using SfUi.Core;

namespace SfUi.App;

/// <summary>
/// テーマ（配色トークン + モダンなコントロール スタイル）の適用。
/// どちらもアプリ全土のリソースなので、メイン画面も各子ウィンドウも同じ見た目になります
/// （クラシック テーマではコントロール スタイルを外すので従来の見た目に戻ります）。
/// </summary>
public static class AppTheme
{
    private const string TokenMarker = "themes/tokens.";
    private const string ModernControlsMarker = "themes/controls.modern.xaml";

    /// <summary>現在適用中のテーマ識別子。</summary>
    public static string Current { get; private set; } = AppThemes.Classic;

    /// <summary>ダーク/ライトが切り替わったときに通知する（エディタの配色切り替え用）。</summary>
    public static event Action<bool>? DarkModeChanged;

    /// <summary>現在のテーマがダークか。</summary>
    public static bool IsDark => AppThemes.IsDark(Current);

    /// <summary>テーマを適用する。<paramref name="window"/> は互換のために残している（現在は未使用）。</summary>
    public static void Apply(string? theme, Window? window = null)
    {
        var key = AppThemes.Normalize(theme);
        Current = key;

        if (Application.Current is not { } app)
        {
            return;
        }

        RemoveMerged(app.Resources, TokenMarker);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"Themes/Tokens.{FileName(key)}.xaml", UriKind.Relative),
        });

        RemoveMerged(app.Resources, ModernControlsMarker);
        if (AppThemes.HasModernChrome(key))
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("Themes/Controls.Modern.xaml", UriKind.Relative),
            });
        }

        DarkModeChanged?.Invoke(AppThemes.IsDark(key));
    }

    private static void RemoveMerged(ResourceDictionary target, string marker)
    {
        for (var i = target.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var source = target.MergedDictionaries[i].Source?.OriginalString;
            if (source is not null && source.Replace('\\', '/').Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                target.MergedDictionaries.RemoveAt(i);
            }
        }
    }

    private static string FileName(string key) => key switch
    {
        AppThemes.Modern => "Modern",
        AppThemes.Dark => "Dark",
        AppThemes.Salesforce => "Salesforce",
        _ => "Classic",
    };
}
