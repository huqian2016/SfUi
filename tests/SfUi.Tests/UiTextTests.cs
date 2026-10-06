using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>グローバルな言語状態を変更するため、他のローカライズ系テストと直列実行する。</summary>
[Collection("Localization")]
public class UiTextTests
{
    [Fact]
    public void AllLanguages_HaveSameKeys()
    {
        var english = UiText.EnglishKeys.ToHashSet();
        Assert.NotEmpty(english);

        foreach (var (name, keys) in new (string, IReadOnlyCollection<string>)[]
        {
            ("ja", UiText.JapaneseKeys),
            ("zh", UiText.ChineseKeys),
            ("ko", UiText.KoreanKeys),
        })
        {
            var set = keys.ToHashSet();
            var missing = english.Except(set).OrderBy(k => k).ToList();
            var extra = set.Except(english).OrderBy(k => k).ToList();

            Assert.True(missing.Count == 0, $"missing in {name}: {string.Join(", ", missing.Take(20))}");
            Assert.True(extra.Count == 0, $"extra in {name}: {string.Join(", ", extra.Take(20))}");
        }
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

            UiText.SetLanguage(UiText.Chinese);
            Assert.Equal("就绪", UiText.T("Common_Ready"));

            UiText.SetLanguage(UiText.Korean);
            Assert.Equal("준비됨", UiText.T("Common_Ready"));
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
    public void T_ShortcutModifier_UsesPlatformLabel()
    {
        // Quick_Hint の {mod} が OS 別の修飾キー名（Windows: Ctrl / macOS: Cmd）に置換される。
        Assert.Equal($"Double-click / {PlatformInfo.ShortcutModifierLabel}+<number> to run, right-click to remove",
            UiText.T("Quick_Hint"));
    }

    [Fact]
    public void SetLanguage_AcceptsAliases_AndOtherwiseFallsBackToEnglish()
    {
        try
        {
            UiText.SetLanguage("日本語");
            Assert.Equal(UiText.Japanese, UiText.Language);

            UiText.SetLanguage("简体中文");
            Assert.Equal(UiText.Chinese, UiText.Language);

            UiText.SetLanguage("한국어");
            Assert.Equal(UiText.Korean, UiText.Language);

            UiText.SetLanguage("zh");
            Assert.Equal(UiText.Chinese, UiText.Language);

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
