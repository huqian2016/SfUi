namespace SfUi.Core;

/// <summary>危険操作（実行前確認が必要なコマンド）の判定。</summary>
public static class SfCommandSafety
{
    /// <summary>危険とみなすトークン（delete / deploy / logout 等）。</summary>
    public static readonly string[] DangerousTokens = { "delete", "deploy", "logout", "refresh", "uninstall" };

    /// <summary>引数に危険操作が含まれるか。</summary>
    public static bool IsDangerous(IReadOnlyList<string> arguments, out string? matchedToken)
    {
        foreach (var argument in arguments)
        {
            if (argument.StartsWith('-'))
            {
                continue;
            }

            foreach (var dangerous in DangerousTokens)
            {
                if (string.Equals(argument, dangerous, StringComparison.OrdinalIgnoreCase))
                {
                    matchedToken = dangerous;
                    return true;
                }
            }
        }

        matchedToken = null;
        return false;
    }
}
