using System.Globalization;

namespace SfUi.Core;

/// <summary>
/// UI 文字列の多言語化。キー → 現在言語の文字列を返す。
/// 言語切替は SetLanguage（"en" / "ja"）で行い、LanguageChanged で UI 側が再描画する。
/// </summary>
public static partial class UiText
{
    public const string English = "en";
    public const string Japanese = "ja";

    private static readonly Dictionary<string, string> En = BuildEn();
    private static readonly Dictionary<string, string> Ja = BuildJa();

    private static string _language = English;

    /// <summary>現在の言語（"en" / "ja"）。</summary>
    public static string Language => _language;

    /// <summary>言語が切り替わったときに発火する。</summary>
    public static event Action? LanguageChanged;

    /// <summary>英語のキー一覧（辞書整合性テスト用）。</summary>
    public static IReadOnlyCollection<string> EnglishKeys => En.Keys;

    /// <summary>日本語のキー一覧（辞書整合性テスト用）。</summary>
    public static IReadOnlyCollection<string> JapaneseKeys => Ja.Keys;

    /// <summary>キーが定義されているか。</summary>
    public static bool HasKey(string key) => En.ContainsKey(key);

    /// <summary>言語を設定する（"ja" / "日本語" 以外は英語）。同じ言語なら何もしない。</summary>
    public static void SetLanguage(string? language)
    {
        var normalized = string.Equals(language, Japanese, StringComparison.OrdinalIgnoreCase) || language == "日本語"
            ? Japanese
            : English;

        if (normalized == _language)
        {
            return;
        }

        _language = normalized;
        LanguageChanged?.Invoke();
    }

    /// <summary>キーから現在の言語の文字列を取得する（未定義キーはキーをそのまま返す）。</summary>
    public static string T(string key)
    {
        var dictionary = _language == Japanese ? Ja : En;
        return dictionary.TryGetValue(key, out var text) ? text : key;
    }

    /// <summary>書式付きで取得する（例: T("Msg_CountFmt", 5)）。</summary>
    public static string T(string key, params object?[] args)
    {
        var format = T(key);
        return args.Length == 0 ? format : string.Format(CultureInfo.CurrentCulture, format, args);
    }
}
