using System;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using SfUi.Core;

namespace SfUi.Avalonia;

/// <summary>
/// テーマ（配色トークン）の適用。Avalonia ではコントロールの外観は Fluent テーマが担うため、
/// ダークのときは <see cref="ThemeVariant.Dark"/> に切り替え、配色トークンだけを差し替えます。
/// </summary>
public static class AppTheme
{
    private const string TokenMarker = "themes/tokens.";

    /// <summary>現在適用中のテーマ識別子。</summary>
    public static string Current { get; private set; } = AppThemes.Classic;

    /// <summary>ダーク/ライトが切り替わったときに通知する（エディタの配色切り替え用）。</summary>
    public static event Action<bool>? DarkModeChanged;

    /// <summary>現在のテーマがダークか。</summary>
    public static bool IsDark => AppThemes.IsDark(Current);

    /// <summary>テーマを適用する。</summary>
    public static void Apply(string? theme)
    {
        var key = AppThemes.Normalize(theme);
        Current = key;

        if (Application.Current is not { } app)
        {
            return;
        }

        // 1) Fluent テーマの明暗（ダークのみダーク）
        app.RequestedThemeVariant = AppThemes.IsDark(key) ? ThemeVariant.Dark : ThemeVariant.Light;

        // 2) 配色トークンを差し替える
        var merged = app.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var source = (merged[i] as ResourceInclude)?.Source?.OriginalString;
            if (source is not null && source.Contains(TokenMarker, StringComparison.OrdinalIgnoreCase))
            {
                merged.RemoveAt(i);
            }
        }

        var uri = new Uri($"avares://SfUi.Avalonia/Themes/Tokens.{FileName(key)}.axaml");
        merged.Add(new ResourceInclude(uri) { Source = uri });

        DarkModeChanged?.Invoke(AppThemes.IsDark(key));
    }

    private static string FileName(string key) => key switch
    {
        AppThemes.Modern => "Modern",
        AppThemes.Dark => "Dark",
        AppThemes.Salesforce => "Salesforce",
        _ => "Classic",
    };
}
