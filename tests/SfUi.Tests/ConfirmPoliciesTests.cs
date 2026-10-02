using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>ラベル検証はグローバル言語に依存するため直列実行する。</summary>
[Collection("Localization")]
public class ConfirmPoliciesTests
{
    [Theory]
    [InlineData(ConfirmPolicies.Dangerous, true, true)]
    [InlineData(ConfirmPolicies.Dangerous, false, false)]
    [InlineData(ConfirmPolicies.Always, true, true)]
    [InlineData(ConfirmPolicies.Always, false, true)]
    [InlineData(ConfirmPolicies.Never, true, false)]
    [InlineData(ConfirmPolicies.Never, false, false)]
    public void ShouldConfirm_MatchesPolicy(string policy, bool isDangerous, bool expected)
        => Assert.Equal(expected, ConfirmPolicies.ShouldConfirm(policy, isDangerous));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void ShouldConfirm_UnknownPolicy_FallsBackToDangerousOnly(string? policy)
    {
        Assert.True(ConfirmPolicies.ShouldConfirm(policy, isDangerous: true));
        Assert.False(ConfirmPolicies.ShouldConfirm(policy, isDangerous: false));
    }

    [Theory]
    [InlineData(ConfirmPolicies.Dangerous, "危険操作のみ確認")]
    [InlineData(ConfirmPolicies.Always, "常に確認")]
    [InlineData(ConfirmPolicies.Never, "確認しない")]
    public void ToLabel_And_FromLabel_RoundTrip(string policy, string label)
    {
        try
        {
            UiText.SetLanguage(UiText.Japanese);
            Assert.Equal(label, ConfirmPolicies.ToLabel(policy));
            Assert.Equal(policy, ConfirmPolicies.FromLabel(label));
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }

    [Fact]
    public void FromLabel_Unknown_DefaultsToDangerous()
        => Assert.Equal(ConfirmPolicies.Dangerous, ConfirmPolicies.FromLabel("謎"));
}
