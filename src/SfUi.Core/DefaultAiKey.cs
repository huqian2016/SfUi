using System.Text;

namespace SfUi.Core;

/// <summary>
/// ダウンロード直後でも AI チャットを試せるようにするための内蔵 DeepSeek API キー。
/// XOR + Base64 による難読化のみで、暗号学的な保護ではない（バイナリからは復元可能）。
/// 設定（settings.json）や環境変数 DEEPSEEK_API_KEY があればそちらが優先される。
/// </summary>
public static class DefaultAiKey
{
    private static readonly byte[] Mask = Encoding.UTF8.GetBytes("SfUi-DeepSeek-2026");

    private const string Encoded = "IA14UExzAFMSalJSDR1RBARUMl83XE8hAFVEZFFcXR4FUVA=";

    /// <summary>難読化されたキーを復元する（復元に失敗した場合は null）。</summary>
    public static string? Value
    {
        get
        {
            try
            {
                var bytes = Convert.FromBase64String(Encoded);
                for (var i = 0; i < bytes.Length; i++)
                {
                    bytes[i] ^= Mask[i % Mask.Length];
                }

                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }
    }
}
