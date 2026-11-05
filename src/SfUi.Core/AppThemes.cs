namespace SfUi.Core;

/// <summary>
/// UI テーマの識別子。実際の配色は各 UI プロジェクトの
/// <c>Themes/Tokens.&lt;name&gt;.xaml</c>（WPF）/ <c>Themes/Tokens.&lt;name&gt;.axaml</c>（Avalonia）が持ちます。
/// </summary>
public static class AppThemes
{
    /// <summary>従来の配色（WPF 既定のコントロール外観）。</summary>
    public const string Classic = "classic";

    /// <summary>モダン ライト（Fluent / Windows 11 風）。</summary>
    public const string Modern = "modern";

    /// <summary>ダーク（開発者ツール風）。</summary>
    public const string Dark = "dark";

    /// <summary>Salesforce（Lightning Design System）風。</summary>
    public const string Salesforce = "salesforce";

    /// <summary>選択できるテーマ（表示順）。</summary>
    public static readonly IReadOnlyList<string> All = new[] { Classic, Modern, Dark, Salesforce };

    /// <summary>未知の値は <see cref="Classic"/> に丸める。</summary>
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Modern => Modern,
        Dark => Dark,
        Salesforce => Salesforce,
        _ => Classic,
    };

    /// <summary>テーマの表示名（現在の UI 言語）。</summary>
    public static string ToLabel(string? key) => Normalize(key) switch
    {
        Modern => UiText.T("Theme_Modern"),
        Dark => UiText.T("Theme_Dark"),
        Salesforce => UiText.T("Theme_Salesforce"),
        _ => UiText.T("Theme_Classic"),
    };

    /// <summary>表示名 → テーマ識別子。</summary>
    public static string FromLabel(string? label)
    {
        foreach (var key in All)
        {
            if (string.Equals(ToLabel(key), label, StringComparison.Ordinal))
            {
                return key;
            }
        }

        return Classic;
    }

    /// <summary>モダンなコントロール外観を使うテーマか（クラシックのみ WPF 既定のまま）。</summary>
    public static bool HasModernChrome(string? key) => Normalize(key) != Classic;

    /// <summary>背景が暗いテーマか（エディタやアイコンの色調整に使う）。</summary>
    public static bool IsDark(string? key) => Normalize(key) == Dark;
}
