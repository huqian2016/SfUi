using System;
using System.Windows;
using SfUi.Core;

namespace SfUi.App;

/// <summary>
/// テーマ（配色トークン + モダンなコントロール スタイル）の適用。
/// 配色はアプリ全体、コントロールの外観はメイン ウィンドウだけに適用します
/// （クラシック テーマではコントロール スタイルを外すので従来の見た目に戻ります）。
/// </summary>
public static class AppTheme
{
    private const string TokenMarker = "themes/tokens.";
    private const string ModernControlsMarker = "themes/controls.modern.xaml";

    /// <summary>現在適用中のテーマ識別子。</summary>
    public static string Current { get; private set; } = AppThemes.Classic;

    /// <summary>テーマを適用する。<paramref name="window"/> を渡すとコントロール スタイルも切り替える。</summary>
    public static void Apply(string? theme, Window? window = null)
    {
        var key = AppThemes.Normalize(theme);
        Current = key;

        if (Application.Current is { } app)
        {
            RemoveMerged(app.Resources, TokenMarker);
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"Themes/Tokens.{FileName(key)}.xaml", UriKind.Relative),
            });
        }

        if (window is not null)
        {
            RemoveMerged(window.Resources, ModernControlsMarker);
            if (AppThemes.HasModernChrome(key))
            {
                window.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("Themes/Controls.Modern.xaml", UriKind.Relative),
                });
            }
        }
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
