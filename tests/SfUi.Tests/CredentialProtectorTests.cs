using System.Security.Cryptography;
using SfUi.App.ViewModels;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class CredentialProtectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-cred-" + Guid.NewGuid().ToString("N"));
    private readonly PlatformCredentialProtector _protector;

    public CredentialProtectorTests()
    {
        Directory.CreateDirectory(_dir);
        _protector = new PlatformCredentialProtector(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void RoundTrip_HidesPlaintext_AndRestores()
    {
        const string secret = "Data Source=db;User Id=admin;Password=s3cret!";
        var protectedText = _protector.Protect(secret);

        Assert.True(_protector.IsProtected(protectedText));
        Assert.StartsWith(PlatformInfo.IsWindows ? "dpapi:" : "aesgcm:", protectedText);
        Assert.DoesNotContain("s3cret", protectedText);
        Assert.DoesNotContain("Password", protectedText);
        Assert.Equal(secret, _protector.Unprotect(protectedText));
    }

    [Fact]
    public void Protect_EmptyOrAlreadyProtected_Unchanged()
    {
        Assert.Equal("", _protector.Protect(""));
        var protectedText = _protector.Protect("x");
        Assert.Equal(protectedText, _protector.Protect(protectedText));
    }

    [Fact]
    public void Unprotect_PlaintextOrEmpty_AsIs()
    {
        Assert.Equal("plain text", _protector.Unprotect("plain text"));
        Assert.Equal("", _protector.Unprotect(""));
    }

    [Fact]
    public void Unprotect_Corrupt_AsIs()
    {
        var prefix = PlatformInfo.IsWindows ? "dpapi:" : "aesgcm:";
        var corrupt = prefix + Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        Assert.Equal(corrupt, _protector.Unprotect(corrupt));
    }

    [Fact]
    public void Unprotect_DpapiPayload_OnNonWindows_AsIs()
    {
        if (PlatformInfo.IsWindows)
        {
            return;   // Windows では復元を試みるため対象外
        }

        var text = "dpapi:" + Convert.ToBase64String(new byte[] { 9, 9, 9 });
        Assert.Equal(text, _protector.Unprotect(text));
    }

    [Fact]
    public void AesEncrypt_Decrypt_RoundTrip()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var payload = PlatformCredentialProtector.AesEncrypt("日本語テキスト too", key);

        Assert.Equal("日本語テキスト too", PlatformCredentialProtector.AesDecrypt(payload, key));
        Assert.NotEqual(payload, PlatformCredentialProtector.AesEncrypt("日本語テキスト too", key));   // nonce は毎回異なる
    }

    [Fact]
    public void AesDecrypt_WrongKey_Throws()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var wrong = RandomNumberGenerator.GetBytes(32);
        var payload = PlatformCredentialProtector.AesEncrypt("secret", key);

        Assert.ThrowsAny<CryptographicException>(() => PlatformCredentialProtector.AesDecrypt(payload, wrong));
    }
}

public class CredentialMaskTests
{
    [Theory]
    [InlineData("Password=s3cret;Data Source=db", "s3cret")]
    [InlineData("pwd=abc;x=1", "abc")]
    [InlineData("Token=tok123", "tok123")]
    [InlineData("access_token=aaa", "aaa")]
    [InlineData("ApiKey=key-1", "key-1")]
    [InlineData("api_key=key-2", "key-2")]
    [InlineData("client_secret=cs1", "cs1")]
    public void Mask_RemovesSecretValues(string text, string secret)
    {
        var masked = CredentialMask.Mask(text);
        Assert.DoesNotContain(secret, masked);
        Assert.Contains("***", masked);
    }

    [Fact]
    public void Mask_NormalText_Unchanged()
    {
        const string text = "SELECT Id FROM Account WHERE Name = 'x'";
        Assert.Equal(text, CredentialMask.Mask(text));
        Assert.Equal(string.Empty, CredentialMask.Mask(null));
    }
}

public class EtlConnectionSecretsTests
{
    [Fact]
    public void ProtectThenUnprotect_RestoresAllSecretFields()
    {
        var protector = new PlatformCredentialProtector(Path.Combine(Path.GetTempPath(), "sfui-cred-flat"));
        var connection = new EtlConnection
        {
            SourceDbConnectionString = "Data Source=s.db;Password=p1",
            TargetDbConnectionString = "Server=x;Password=p2",
            SourceRestToken = "tok",
            SourceRestPassword = "rp",
            SourceRestHeaders = "X-Api-Key: k1",
            SourceRestUrl = "https://example.com",   // 非秘密はそのまま
        };

        EtlConnectionSecrets.ProtectInPlace(new[] { connection }, protector);
        Assert.True(protector.IsProtected(connection.SourceDbConnectionString));
        Assert.True(protector.IsProtected(connection.TargetDbConnectionString));
        Assert.True(protector.IsProtected(connection.SourceRestToken));
        Assert.True(protector.IsProtected(connection.SourceRestPassword));
        Assert.True(protector.IsProtected(connection.SourceRestHeaders));
        Assert.Equal("https://example.com", connection.SourceRestUrl);

        EtlConnectionSecrets.UnprotectInPlace(new[] { connection }, protector);
        Assert.Equal("Data Source=s.db;Password=p1", connection.SourceDbConnectionString);
        Assert.Equal("Server=x;Password=p2", connection.TargetDbConnectionString);
        Assert.Equal("tok", connection.SourceRestToken);
        Assert.Equal("rp", connection.SourceRestPassword);
        Assert.Equal("X-Api-Key: k1", connection.SourceRestHeaders);
    }
}
