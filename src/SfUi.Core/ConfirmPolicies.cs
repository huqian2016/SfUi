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

    /// <summary>ポリシーの表示ラベル（現在の言語）。</summary>
    public static string ToLabel(string? policy) => policy switch
    {
        Always => UiText.T("Policy_Always"),
        Never => UiText.T("Policy_Never"),
        _ => UiText.T("Policy_Dangerous"),
    };

    /// <summary>表示ラベルからポリシーへ（不一致は既定の dangerous）。</summary>
    public static string FromLabel(string? label)
    {
        if (label == UiText.T("Policy_Always"))
        {
            return Always;
        }

        if (label == UiText.T("Policy_Never"))
        {
            return Never;
        }

        return Dangerous;
    }

    /// <summary>この操作の実行前に確認を表示すべきか。</summary>
    public static bool ShouldConfirm(string? policy, bool isDangerous) => policy switch
    {
        Never => false,
        Always => true,
        _ => isDangerous,
    };
}
