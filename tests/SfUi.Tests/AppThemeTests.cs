using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>テーマ識別子と表示名の変換（テーマ切替コンボの往復）。</summary>
[Collection("Localization")]
public sealed class AppThemeTests
{
    [Theory]
    [InlineData(null, AppThemes.Classic)]
    [InlineData("", AppThemes.Classic)]
    [InlineData("Classic", AppThemes.Classic)]
    [InlineData(" modern ", AppThemes.Modern)]
    [InlineData("DARK", AppThemes.Dark)]
    [InlineData("salesforce", AppThemes.Salesforce)]
    [InlineData("unknown", AppThemes.Classic)]
    public void Normalize_FallsBackToClassic(string? value, string expected) =>
        Assert.Equal(expected, AppThemes.Normalize(value));

    [Fact]
    public void All_ContainsEveryThemeOnce()
    {
        Assert.Equal(4, AppThemes.All.Count);
        Assert.Equal(AppThemes.All.Count, AppThemes.All.Distinct().Count());
        Assert.Contains(AppThemes.Classic, AppThemes.All);
        Assert.Contains(AppThemes.Modern, AppThemes.All);
        Assert.Contains(AppThemes.Dark, AppThemes.All);
        Assert.Contains(AppThemes.Salesforce, AppThemes.All);
    }

    [Fact]
    public void Labels_RoundTripInEveryLanguage()
    {
        var original = UiText.Language;
        try
        {
            foreach (var lang in new[] { UiText.English, UiText.Japanese, UiText.Chinese, UiText.Korean })
            {
                UiText.SetLanguage(lang);
                foreach (var key in AppThemes.All)
                {
                    var label = AppThemes.ToLabel(key);
                    Assert.False(string.IsNullOrWhiteSpace(label));
                    Assert.Equal(key, AppThemes.FromLabel(label));
                }
            }
        }
        finally
        {
            UiText.SetLanguage(original);
        }
    }

    [Fact]
    public void FromLabel_UnknownLabelFallsBackToClassic() =>
        Assert.Equal(AppThemes.Classic, AppThemes.FromLabel("no such theme"));

    [Theory]
    [InlineData(AppThemes.Classic, false)]
    [InlineData(AppThemes.Modern, true)]
    [InlineData(AppThemes.Dark, true)]
    [InlineData(AppThemes.Salesforce, true)]
    public void HasModernChrome_IsFalseOnlyForClassic(string theme, bool expected) =>
        Assert.Equal(expected, AppThemes.HasModernChrome(theme));

    [Fact]
    public void IsDark_IsTrueOnlyForDarkTheme()
    {
        Assert.True(AppThemes.IsDark(AppThemes.Dark));
        Assert.False(AppThemes.IsDark(AppThemes.Modern));
        Assert.False(AppThemes.IsDark(null));
    }
}
