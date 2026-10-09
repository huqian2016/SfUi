using System.Security.Cryptography;
using System.Text;

namespace SfUi.Core;

/// <summary>資格情報（接続文字列・トークン等）の保護。ファイルには保護済みテキストのみを保存する。</summary>
public interface ICredentialProtector
{
    /// <summary>平文を保護済みテキスト（プレフィックス付き）へ変換する。空・既に保護済みならそのまま返す。</summary>
    string Protect(string plaintext);

    /// <summary>保護済みテキストを平文へ戻す。保護されていない・復元できないテキストはそのまま返す。</summary>
    string Unprotect(string text);

    /// <summary>保護済みテキストか。</summary>
    bool IsProtected(string text);
}

/// <summary>
/// プラットフォーム別の資格情報保護:
/// Windows は DPAPI（CurrentUser）、その他は AES-GCM + ローカル鍵ファイル（Unix パーミッション 600）。
/// プレフィックス: "dpapi:" / "aesgcm:"。プレフィックスのないテキストは平文として扱う（後方互換）。
/// </summary>
public sealed class PlatformCredentialProtector : ICredentialProtector
{
    public const string DpapiPrefix = "dpapi:";

    public const string AesPrefix = "aesgcm:";

    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string? _keyFilePath;
    private readonly object _gate = new();

    /// <param name="appDataDirectory">AES 鍵ファイルを置くディレクトリ（Windows 以外で使用）。null なら OS 既定。</param>
    public PlatformCredentialProtector(string? appDataDirectory = null)
    {
        if (!PlatformInfo.IsWindows)
        {
            var baseDirectory = appDataDirectory ?? DefaultAppDataDirectory();
            _keyFilePath = Path.Combine(baseDirectory, "SfUi", "credential.key");
        }
    }

    public bool IsProtected(string text)
        => !string.IsNullOrEmpty(text) &&
           (text.StartsWith(DpapiPrefix, StringComparison.Ordinal) || text.StartsWith(AesPrefix, StringComparison.Ordinal));

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext) || IsProtected(plaintext))
        {
            return plaintext;
        }

        return PlatformInfo.IsWindows ? DpapiProtect(plaintext) : AesPrefix + AesEncrypt(plaintext, GetOrCreateKey());
    }

    public string Unprotect(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        try
        {
            if (text.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                return PlatformInfo.IsWindows ? DpapiUnprotect(text) : text;
            }

            if (text.StartsWith(AesPrefix, StringComparison.Ordinal))
            {
                return AesDecrypt(text[AesPrefix.Length..], GetOrCreateKey());
            }
        }
        catch
        {
            // 復元できない（別ユーザー / 別マシン / 破損）場合はそのまま返す
        }

        return text;
    }

    private static string DpapiProtect(string plaintext)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser);
        return DpapiPrefix + Convert.ToBase64String(bytes);
    }

    private static string DpapiUnprotect(string text)
    {
        var bytes = ProtectedData.Unprotect(
            Convert.FromBase64String(text[DpapiPrefix.Length..]), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>AES-GCM で暗号化し、nonce + tag + cipher を Base64 で返す（テスト用に internal）。</summary>
    internal static string AesEncrypt(string plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }

        var all = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(all, 0);
        tag.CopyTo(all, NonceSize);
        cipher.CopyTo(all, NonceSize + TagSize);
        return Convert.ToBase64String(all);
    }

    /// <summary>AES-GCM の Base64（nonce + tag + cipher）を復号する（テスト用に internal）。</summary>
    internal static string AesDecrypt(string payload, byte[] key)
    {
        var all = Convert.FromBase64String(payload);
        if (all.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("payload too short");
        }

        var nonce = all.AsSpan(0, NonceSize);
        var tag = all.AsSpan(NonceSize, TagSize);
        var cipher = all.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Decrypt(nonce, cipher, tag, plain);
        }

        return Encoding.UTF8.GetString(plain);
    }

    private byte[] GetOrCreateKey()
    {
        lock (_gate)
        {
            var path = _keyFilePath!;
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == KeySize)
                {
                    return existing;
                }
            }

            var key = RandomNumberGenerator.GetBytes(KeySize);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, key);
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                // パーミッション設定に失敗しても動作は継続
            }

            return key;
        }
    }

    private static string DefaultAppDataDirectory()
        => PlatformInfo.IsMacOS
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
}
