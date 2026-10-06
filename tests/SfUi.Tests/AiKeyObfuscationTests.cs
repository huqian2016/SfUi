using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class AiKeyObfuscationTests
{
    [Fact]
    public void Normalize_ReturnsPlainText_AsIs()
        => Assert.Equal("sk-plain", AiKeyObfuscation.Normalize("  sk-plain  "));

    [Fact]
    public void Normalize_ReturnsNull_ForNullOrBlank()
    {
        Assert.Null(AiKeyObfuscation.Normalize(null));
        Assert.Null(AiKeyObfuscation.Normalize("   "));
    }

    [Fact]
    public void Protect_ThenNormalize_RoundTrips()
    {
        var protectedValue = AiKeyObfuscation.Protect("sk-abc-123");

        Assert.NotNull(protectedValue);
        Assert.StartsWith(AiKeyObfuscation.Prefix, protectedValue);
        Assert.Equal("sk-abc-123", AiKeyObfuscation.Normalize(protectedValue));
    }

    [Fact]
    public void Protect_ReturnsNull_ForNullOrBlank()
    {
        Assert.Null(AiKeyObfuscation.Protect(null));
        Assert.Null(AiKeyObfuscation.Protect(" "));
    }

    [Fact]
    public void Protect_IsDeterministicAndIdempotent()
    {
        var first = AiKeyObfuscation.Protect("sk-abc");

        Assert.Equal(first, AiKeyObfuscation.Protect("sk-abc")); // 表示が毎回変わらない（決定論的）
        Assert.Equal(first, AiKeyObfuscation.Protect(first));    // 二重適用しても結果が変わらない（冪等）
    }

    [Fact]
    public void Normalize_ReturnsNull_ForUnreadableProtectedValue()
    {
        Assert.Null(AiKeyObfuscation.Normalize("enc1:%%%not-base64%%%"));
        Assert.Null(AiKeyObfuscation.Normalize("enc1:"));
    }

    [Fact]
    public void Protect_KeepsUnreadableValue_AsIs()
    {
        // 復元できない enc1: 値（手編集ミス等）はデータを失わないようそのまま保持する
        Assert.Equal("enc1:%%%not-base64%%%", AiKeyObfuscation.Protect("enc1:%%%not-base64%%%"));
    }

    [Fact]
    public void Protect_ProducesValue_ThatNoLongerContainsPlainText()
    {
        var protectedValue = AiKeyObfuscation.Protect("sk-very-secret");

        Assert.NotNull(protectedValue);
        Assert.DoesNotContain("sk-very-secret", protectedValue);
    }
}
