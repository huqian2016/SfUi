using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>ManualLinks（各ウィンドウのヘルプ URL）の組み立てを検証する。</summary>
[Collection("Localization")]
public class ManualLinksTests
{
    /// <summary>ユーザー合意済みの例: 日本語 UI の組織情報ウィンドウの URL。</summary>
    [Fact]
    public void OrgInfo_Japanese_MatchesAgreedExample()
    {
        var url = ManualLinks.BuildUrl(ManualLinks.Topic.OrgInfo, "ja");

        Assert.Equal(
            "https://github.com/huqian2016/SfUi/blob/main/docs/manual/ja.md#7-%E7%B5%84%E7%B9%94%E6%83%85%E5%A0%B1org-info",
            url);
    }

    /// <summary>全ウィンドウ × 全言語で、ベース URL + 言語ファイル + エンコード済みアンカーになる。</summary>
    [Fact]
    public void AllTopics_HaveEncodedAnchor_ForAllLanguages()
    {
        foreach (var language in new[] { "en", "ja", "zh", "ko" })
        {
            foreach (var topic in Enum.GetValues<ManualLinks.Topic>())
            {
                var url = ManualLinks.BuildUrl(topic, language);

                Assert.StartsWith($"{ManualLinks.BaseUrl}{language}.md#", url);
                var anchor = url[(url.IndexOf('#') + 1)..];
                Assert.False(string.IsNullOrWhiteSpace(anchor));
                Assert.DoesNotContain(" ", anchor);
                Assert.True(anchor.All(c => c < 128), $"アンカーは URL エンコード済みの ASCII であること: {url}");
            }
        }
    }

    /// <summary>不明な言語コードは英語マニュアルにフォールバックする。</summary>
    [Fact]
    public void UnknownLanguage_FallsBackToEnglish()
    {
        var url = ManualLinks.BuildUrl(ManualLinks.Topic.DataIo, "fr");

        Assert.StartsWith($"{ManualLinks.BaseUrl}en.md#8-data-io", url);
    }

    /// <summary>言語未指定は現在の UI 言語（既定: 英語）を使う。</summary>
    [Fact]
    public void BuildUrl_WithoutLanguage_UsesCurrentUiLanguage()
    {
        Assert.Equal(UiText.English, UiText.Language);

        var url = ManualLinks.BuildUrl(ManualLinks.Topic.LogAnalyzer);

        Assert.StartsWith($"{ManualLinks.BaseUrl}en.md#", url);
    }

    /// <summary>About ウィンドウ用: 選択中の言語のマニュアル先頭ページ（アンカーなし）。</summary>
    [Theory]
    [InlineData("en", "https://github.com/huqian2016/SfUi/blob/main/docs/manual/en.md")]
    [InlineData("ja", "https://github.com/huqian2016/SfUi/blob/main/docs/manual/ja.md")]
    [InlineData("zh", "https://github.com/huqian2016/SfUi/blob/main/docs/manual/zh.md")]
    [InlineData("ko", "https://github.com/huqian2016/SfUi/blob/main/docs/manual/ko.md")]
    public void BuildManualUrl_UsesLanguageFile(string language, string expected)
    {
        Assert.Equal(expected, ManualLinks.BuildManualUrl(language));
    }

    /// <summary>不明な言語コードは英語マニュアルにフォールバックする。</summary>
    [Fact]
    public void BuildManualUrl_UnknownLanguage_FallsBackToEnglish()
    {
        Assert.Equal("https://github.com/huqian2016/SfUi/blob/main/docs/manual/en.md", ManualLinks.BuildManualUrl("fr"));
    }
}
