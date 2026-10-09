using System.Text.RegularExpressions;

namespace SfUi.Core;

/// <summary>ログ・監査出力へ秘密情報を出さないためのマスキング。</summary>
public static class CredentialMask
{
    private static readonly Regex SecretPattern = new(
        @"(?i)\b(password|pwd|token|access[_-]?token|api[_-]?key|secret|client[_-]?secret)\s*=\s*([^;""\s]+)",
        RegexOptions.Compiled);

    /// <summary>"key=value" 形式の秘密値を *** に置き換える（Password=… / Token=… / ApiKey=… など）。</summary>
    public static string Mask(string? text)
        => string.IsNullOrEmpty(text) ? text ?? string.Empty : SecretPattern.Replace(text, "$1=***");
}
