using System.Globalization;

namespace SfUi.Core;

/// <summary>
/// UI 文字列の多言語化。キー → 現在言語の文字列を返す。
/// 言語切替は SetLanguage（"en" / "ja" / "zh" / "ko"）で行い、LanguageChanged で UI 側が再描画する。
/// </summary>
public static partial class UiText
{
    public const string English = "en";
    public const string Japanese = "ja";
    public const string Chinese = "zh";
    public const string Korean = "ko";

    private static readonly Dictionary<string, string> En = BuildEn();
    private static readonly Dictionary<string, string> Ja = BuildJa();
    private static readonly Dictionary<string, string> Zh = BuildZh();
    private static readonly Dictionary<string, string> Ko = BuildKo();

    private static string _language = English;

    /// <summary>現在の言語（"en" / "ja" / "zh" / "ko"）。</summary>
    public static string Language => _language;

    /// <summary>言語が切り替わったときに発火する。</summary>
    public static event Action? LanguageChanged;

    /// <summary>英語のキー一覧（辞書整合性テスト用）。</summary>
    public static IReadOnlyCollection<string> EnglishKeys => En.Keys;

    /// <summary>日本語のキー一覧（辞書整合性テスト用）。</summary>
    public static IReadOnlyCollection<string> JapaneseKeys => Ja.Keys;

    /// <summary>中国語（簡体）のキー一覧（辞書整合性テスト用）。</summary>
    public static IReadOnlyCollection<string> ChineseKeys => Zh.Keys;

    /// <summary>韓国語のキー一覧（辞書整合性テスト用）。</summary>
    public static IReadOnlyCollection<string> KoreanKeys => Ko.Keys;

    /// <summary>キーが定義されているか。</summary>
    public static bool HasKey(string key) => En.ContainsKey(key);

    /// <summary>言語を設定する（"ja" / "zh" / "ko" と各言語の別名以外は英語）。同じ言語なら何もしない。</summary>
    public static void SetLanguage(string? language)
    {
        var normalized = Normalize(language);

        if (normalized == _language)
        {
            return;
        }

        _language = normalized;
        LanguageChanged?.Invoke();
    }

    /// <summary>言語コード・別名（"日本語" / "简体中文" / "한국어" など）を正規化する。</summary>
    private static string Normalize(string? language)
    {
        var value = language?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return English;
        }

        switch (value)
        {
            case "日本語":
                return Japanese;
            case "中文" or "中文（简体）" or "中文(简体)" or "简体中文":
                return Chinese;
            case "한국어":
                return Korean;
        }

        if (value.Equals(Japanese, StringComparison.OrdinalIgnoreCase))
        {
            return Japanese;
        }

        if (value.Equals(Chinese, StringComparison.OrdinalIgnoreCase))
        {
            return Chinese;
        }

        if (value.Equals(Korean, StringComparison.OrdinalIgnoreCase))
        {
            return Korean;
        }

        return English;
    }

    /// <summary>キーから現在の言語の文字列を取得する（未定義キーはキーをそのまま返す）。</summary>
    public static string T(string key)
    {
        var dictionary = _language switch
        {
            Japanese => Ja,
            Chinese => Zh,
            Korean => Ko,
            _ => En,
        };
        return dictionary.TryGetValue(key, out var text) ? text : key;
    }

    /// <summary>書式付きで取得する（例: T("Msg_CountFmt", 5)）。</summary>
    public static string T(string key, params object?[] args)
    {
        var format = T(key);
        return args.Length == 0 ? format : string.Format(CultureInfo.CurrentCulture, format, args);
    }
}
