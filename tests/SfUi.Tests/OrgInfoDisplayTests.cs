using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>表示言語を変更するため、ローカライズ系テストと直列実行する。</summary>
[Collection("Localization")]
public class OrgInfoDisplayTests
{
    private static OrgInfoRow Row(string id, params (string Key, string? Value)[] cells)
    {
        var row = new OrgInfoRow { Id = id, Summary = id };
        foreach (var (key, value) in cells)
        {
            row.Cells[key] = value;
        }

        return row;
    }

    [Fact]
    public void FormatCell_ResolvesOverviewItemLabel()
    {
        try
        {
            UiText.SetLanguage(UiText.English);
            var row = Row("orgName", ("value", "HKS"));
            Assert.Equal("Org Name", OrgInfoDisplay.FormatCell(OrgInfoSections.Overview, row, "item", null));
            Assert.Equal("HKS", OrgInfoDisplay.FormatCell(OrgInfoSections.Overview, row, "value", "HKS"));

            UiText.SetLanguage(UiText.Japanese);
            Assert.Equal("組織名", OrgInfoDisplay.FormatCell(OrgInfoSections.Overview, row, "item", null));
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }

    [Fact]
    public void FormatCell_LocalizesTokens()
    {
        try
        {
            UiText.SetLanguage(UiText.English);

            var environment = Row("environment", ("value", OrgInfoTokens.Production));
            Assert.Equal("Production", OrgInfoDisplay.FormatCell(OrgInfoSections.Overview, environment, "environment", OrgInfoTokens.Production));

            var user = Row("u1", ("active", OrgInfoTokens.True));
            Assert.Equal("Yes", OrgInfoDisplay.FormatCell(OrgInfoSections.Users, user, "active", OrgInfoTokens.True));

            var objectRow = Row("My_Object__c", ("kind", OrgInfoTokens.Custom));
            Assert.Equal("Custom", OrgInfoDisplay.FormatCell(OrgInfoSections.Objects, objectRow, "kind", OrgInfoTokens.Custom));

            var owd = Row("Account", ("target", "Account"), ("source", OrgInfoTokens.OrgDefault));
            Assert.Equal("Account", OrgInfoDisplay.FormatCell(OrgInfoSections.Owds, owd, "target", "Account"));
            Assert.Equal("Org default", OrgInfoDisplay.FormatCell(OrgInfoSections.Owds, owd, "source", OrgInfoTokens.OrgDefault));

            var field = Row("MyField__c", ("custom", OrgInfoTokens.False));
            Assert.Equal("No", OrgInfoDisplay.FormatCell(OrgInfoSections.Fields("Account"), field, "custom", OrgInfoTokens.False));
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }

    [Fact]
    public void FormatCell_UnknownValuesPassThrough()
    {
        var row = Row("u1", ("name", "Taro"));
        Assert.Equal("Taro", OrgInfoDisplay.FormatCell(OrgInfoSections.Users, row, "name", "Taro"));
        Assert.Null(OrgInfoDisplay.FormatCell(OrgInfoSections.Users, row, "email", null));
    }

    [Fact]
    public void FormatCell_SettingsSection_LocalizesItemAndTokens()
    {
        try
        {
            UiText.SetLanguage(UiText.English);
            var boolRow = Row("lightningLogin", ("value", OrgInfoTokens.True));
            Assert.Equal("Lightning Login Enabled", OrgInfoDisplay.FormatCell(OrgInfoSections.Settings, boolRow, "item", null));
            Assert.Equal("Yes", OrgInfoDisplay.FormatCell(OrgInfoSections.Settings, boolRow, "value", OrgInfoTokens.True));

            var linkRow = Row("sessionSettings", ("value", OrgInfoTokens.LinkOnly));
            Assert.Equal("Link only", OrgInfoDisplay.FormatCell(OrgInfoSections.Settings, linkRow, "value", OrgInfoTokens.LinkOnly));

            UiText.SetLanguage(UiText.Japanese);
            Assert.Equal("リンクのみ", OrgInfoDisplay.FormatCell(OrgInfoSections.Settings, linkRow, "value", OrgInfoTokens.LinkOnly));
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }

    [Fact]
    public void FormatCell_CandidateSections_LocalizeBooleans()
    {
        try
        {
            UiText.SetLanguage(UiText.English);

            var flow = Row("3001", ("active", OrgInfoTokens.True));
            Assert.Equal("Yes", OrgInfoDisplay.FormatCell(OrgInfoSections.Flows, flow, "active", OrgInfoTokens.True));

            var apex = Row("01p1", ("valid", OrgInfoTokens.False));
            Assert.Equal("No", OrgInfoDisplay.FormatCell(OrgInfoSections.ApexClasses, apex, "valid", OrgInfoTokens.False));
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }

    [Fact]
    public void FormatValueToken_MapsCommonTokens()
    {
        try
        {
            UiText.SetLanguage(UiText.English);
            Assert.Equal("Link only", OrgInfoDisplay.FormatValueToken(OrgInfoTokens.LinkOnly));
            Assert.Equal("Yes", OrgInfoDisplay.FormatValueToken(OrgInfoTokens.True));
            Assert.Equal("Custom", OrgInfoDisplay.FormatValueToken(OrgInfoTokens.Custom));
            Assert.Equal("Sandbox", OrgInfoDisplay.FormatValueToken(OrgInfoTokens.Sandbox));
            Assert.Equal("plain", OrgInfoDisplay.FormatValueToken("plain"));
            Assert.Null(OrgInfoDisplay.FormatValueToken(null));
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }

    [Fact]
    public void BuildSearchText_IncludesLabelsAndValues()
    {
        try
        {
            UiText.SetLanguage(UiText.English);
            var row = Row("u1", ("name", "Taro Yamada"), ("email", "taro@example.com"));
            row.Summary = "Taro Yamada";

            var text = OrgInfoDisplay.BuildSearchText(OrgInfoSections.Users, OrgInfoSections.UserColumns, row);

            Assert.Contains("email", text);          // 列ラベル
            Assert.Contains("taro@example.com", text); // 値
            Assert.Contains("taro yamada", text);      // 要約
        }
        finally
        {
            UiText.SetLanguage(UiText.English);
        }
    }
}
