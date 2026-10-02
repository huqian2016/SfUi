using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>グローバルな言語状態を変更するため、他のローカライズ系テストと直列実行する。</summary>
[Collection("Localization")]
public class UiTextTests
{
    [Fact]
    public void EnglishAndJapanese_HaveSameKeys()
    {
        var english = UiText.EnglishKeys.ToHashSet();
        var japanese = UiText.JapaneseKeys.ToHashSet();

        var missingInJa = english.Except(japanese).OrderBy(k => k).ToList();
        var missingInEn = japanese.Except(english).OrderBy(k => k).ToList();

        Assert.Empty(missingInJa);
        Assert.Empty(missingInEn);
        Assert.NotEmpty(english);
    }

    [Fact]
    public void T_ReturnsText_ForCurrentLanguage()
    {
        try
        {
            UiText.SetLanguage(UiText.English);
            Assert.Equal("Ready", UiText.T("Common_Ready"));

            UiText.SetLanguage(UiText.Japanese);
            Assert.Equal("準備完了", UiText.T("Common_Ready"));
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }

    [Fact]
    public void T_WithArguments_FormatsMessage()
    {
        try
        {
            UiText.SetLanguage(UiText.English);
            Assert.Equal("Loaded 3 org(s)", UiText.T("Msg_OrgCountFmt", 3));

            UiText.SetLanguage(UiText.Japanese);
            Assert.Equal("組織 3 件を取得しました", UiText.T("Msg_OrgCountFmt", 3));
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }

    [Fact]
    public void T_UnknownKey_ReturnsKey()
    {
        Assert.Equal("No_Such_Key", UiText.T("No_Such_Key"));
    }

    [Fact]
    public void SetLanguage_AcceptsJapaneseAlias_AndOtherwiseFallsBackToEnglish()
    {
        try
        {
            UiText.SetLanguage("日本語");
            Assert.Equal(UiText.Japanese, UiText.Language);

            UiText.SetLanguage("de");
            Assert.Equal(UiText.English, UiText.Language);
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }

    [Fact]
    public void SetLanguage_FiresChangedOnlyWhenLanguageDiffers()
    {
        try
        {
            UiText.SetLanguage(UiText.English);
            var count = 0;
            void Handler() => count++;

            UiText.LanguageChanged += Handler;
            try
            {
                UiText.SetLanguage(UiText.English); // 変化なし
                UiText.SetLanguage(UiText.Japanese); // 変化あり
                UiText.SetLanguage(UiText.Japanese); // 変化なし
            }
            finally
            {
                UiText.LanguageChanged -= Handler;
            }

            Assert.Equal(1, count);
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }

    [Fact]
    public void TypeLabels_FollowLanguage()
    {
        try
        {
            UiText.SetLanguage(UiText.English);
            Assert.Equal("Apex", HistoryTypes.ToLabel(HistoryTypes.Apex));
            Assert.Equal("Success", new HistoryEntry { Status = "success" }.StatusLabel);

            UiText.SetLanguage(UiText.Japanese);
            Assert.Equal("匿名Apex", HistoryTypes.ToLabel(HistoryTypes.Apex));
            Assert.Equal("成功", new HistoryEntry { Status = "success" }.StatusLabel);
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }

    [Fact]
    public void ConfirmPolicyLabels_FollowLanguage_AndRoundTrip()
    {
        try
        {
            UiText.SetLanguage(UiText.English);
            Assert.Equal("Dangerous only", ConfirmPolicies.ToLabel(ConfirmPolicies.Dangerous));
            Assert.Equal(ConfirmPolicies.Never, ConfirmPolicies.FromLabel(ConfirmPolicies.ToLabel(ConfirmPolicies.Never)));

            UiText.SetLanguage(UiText.Japanese);
            Assert.Equal("危険操作のみ確認", ConfirmPolicies.ToLabel(ConfirmPolicies.Dangerous));
            Assert.Equal(ConfirmPolicies.Always, ConfirmPolicies.FromLabel(ConfirmPolicies.ToLabel(ConfirmPolicies.Always)));
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }
}
