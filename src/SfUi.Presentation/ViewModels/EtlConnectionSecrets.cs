using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>接続マネージャーの秘密情報フィールドの保護 / 復元（保存ファイルには保護済みの値だけを書く）。</summary>
public static class EtlConnectionSecrets
{
    /// <summary>秘密情報フィールド（DB 接続文字列 / REST トークン・パスワード・ヘッダー）を保護する。</summary>
    public static void ProtectInPlace(IEnumerable<EtlConnection> connections, ICredentialProtector protector)
    {
        foreach (var connection in connections)
        {
            connection.SourceDbConnectionString = protector.Protect(connection.SourceDbConnectionString);
            connection.TargetDbConnectionString = protector.Protect(connection.TargetDbConnectionString);
            connection.SourceRestToken = protector.Protect(connection.SourceRestToken);
            connection.SourceRestPassword = protector.Protect(connection.SourceRestPassword);
            connection.SourceRestHeaders = protector.Protect(connection.SourceRestHeaders);
        }
    }

    /// <summary>秘密情報フィールドを平文へ戻す（未保護の値はそのまま）。</summary>
    public static void UnprotectInPlace(IEnumerable<EtlConnection> connections, ICredentialProtector protector)
    {
        foreach (var connection in connections)
        {
            connection.SourceDbConnectionString = protector.Unprotect(connection.SourceDbConnectionString);
            connection.TargetDbConnectionString = protector.Unprotect(connection.TargetDbConnectionString);
            connection.SourceRestToken = protector.Unprotect(connection.SourceRestToken);
            connection.SourceRestPassword = protector.Unprotect(connection.SourceRestPassword);
            connection.SourceRestHeaders = protector.Unprotect(connection.SourceRestHeaders);
        }
    }
}
