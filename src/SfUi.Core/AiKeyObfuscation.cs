using System.Text;

namespace SfUi.Core;

/// <summary>
/// AI API キー値の難読化（enc1: 形式）。
/// XOR + Base64 による難読化のみで暗号学的な保護ではない（バイナリ・アルゴリズムは公開前提で復元可能）。
/// 目的は「settings.json や画面に平文キーをそのまま残さず、うっかりコピーされても外部でそのまま使えない」こと。
/// 平文はそのまま受け付け（後方互換）、enc1: 付きの値は復元して使用する（冪等）。
/// </summary>
public static class AiKeyObfuscation
{
    /// <summary>難読化値の形式プレフィックス。</summary>
    public const string Prefix = "enc1:";

    private static readonly byte[] Mask = Encoding.UTF8.GetBytes("SfUi-AiKey-2026");

    /// <summary>
    /// 平文を難読化する（null / 空は null）。冪等: すでに enc1: の値は復元してから再難読化するため、
    /// 二重適用しても結果は変わらない。復元できない enc1: 値はデータを失わないようそのまま返す。
    /// </summary>
    public static string? Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var plain = Normalize(trimmed);
        if (plain is null)
        {
            // 復元できない enc1: 値（手編集ミス等）はそのまま保持する
            return trimmed;
        }

        return Prefix + Convert.ToBase64String(Xor(Encoding.UTF8.GetBytes(plain)));
    }

    /// <summary>
    /// 使用可能な平文キーへ解決する。平文はそのまま（前後の空白のみ除去）、enc1: は復元する。
    /// null / 空 / 復元不能な enc1: は null（キー未設定として扱う）。
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return trimmed;
        }

        try
        {
            var bytes = Xor(Convert.FromBase64String(trimmed[Prefix.Length..]));
            var plain = Encoding.UTF8.GetString(bytes);
            return string.IsNullOrWhiteSpace(plain) ? null : plain;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[] Xor(byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] ^= Mask[i % Mask.Length];
        }

        return bytes;
    }
}
