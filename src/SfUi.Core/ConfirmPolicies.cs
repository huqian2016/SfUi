namespace SfUi.Core;

/// <summary>実行前確認ポリシー。</summary>
public static class ConfirmPolicies
{
    /// <summary>危険操作のみ確認（既定）。</summary>
    public const string Dangerous = "dangerous";

    /// <summary>常に確認。</summary>
    public const string Always = "always";

    /// <summary>確認しない。</summary>
    public const string Never = "never";

    /// <summary>ポリシーを日本語ラベルへ。</summary>
    public static string ToLabel(string? policy) => policy switch
    {
        Always => "常に確認",
        Never => "確認しない",
        _ => "危険操作のみ確認",
    };

    /// <summary>日本語ラベルからポリシーへ。</summary>
    public static string FromLabel(string? label) => label switch
    {
        "常に確認" => Always,
        "確認しない" => Never,
        _ => Dangerous,
    };

    /// <summary>この操作の実行前に確認を表示すべきか。</summary>
    public static bool ShouldConfirm(string? policy, bool isDangerous) => policy switch
    {
        Never => false,
        Always => true,
        _ => isDangerous,
    };
}
