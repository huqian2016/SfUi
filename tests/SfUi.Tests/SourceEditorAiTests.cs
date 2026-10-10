using SfUi.App.ViewModels;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>
/// AI コード片（AiSnippet / ParseSnippets）のテスト（Phase 6: ソース エディタの AI 支援）。
/// UiText（適用ラベル）を読むため Localization コレクションに参加する。
/// </summary>
[Collection("Localization")]
public class SourceEditorAiTests
{
    [Fact]
    public void ParseSnippets_MapsSourceLanguages()
    {
        var text = "```javascript\nconst a = 1;\n```\n```html\n<div></div>\n```";
        var snippets = AiChatViewModel.ParseSnippets(text);

        Assert.Equal(2, snippets.Count);
        Assert.Equal("javascript", snippets[0].Language);
        Assert.Equal("const a = 1;", snippets[0].Code);
        Assert.Equal("html", snippets[1].Language);
    }

    [Fact]
    public void ParseSnippets_ExtraLanguages_EnableApply()
    {
        var text = "```javascript\nconst a = 1;\n```";
        var extras = new[] { "apex", "javascript", "html", "visualforce", "css" };

        // 既定（メインウィンドウ）では JavaScript に適用ボタンを出さない
        var plain = AiChatViewModel.ParseSnippets(text)[0];
        Assert.False(plain.CanApply);
        Assert.Equal(string.Empty, plain.ApplyLabel);

        // ソース エディタは追加言語に適用ボタンを出す
        var extended = AiChatViewModel.ParseSnippets(text, extras, "Apply to editor")[0];
        Assert.True(extended.CanApply);
        Assert.Equal("Apply to editor", extended.ApplyLabel);
    }

    [Fact]
    public void ParseSnippets_KeepsSoqlApexAndCommandBehavior()
    {
        var text = "```soql\nSELECT Id FROM Account\n```\n```apex\nSystem.debug(1);\n```\n```bash\nsf org list\n```";
        var snippets = AiChatViewModel.ParseSnippets(text);

        Assert.Equal(3, snippets.Count);
        Assert.Equal("soql", snippets[0].Language);
        Assert.Equal("apex", snippets[1].Language);
        Assert.Equal("command", snippets[2].Language);
        Assert.All(snippets, s => Assert.True(s.CanApply));
    }

    [Fact]
    public void ParseSnippets_Css_IsMappedWithoutApplyByDefault()
    {
        var snippets = AiChatViewModel.ParseSnippets("```css\n.a { color: red; }\n```");

        Assert.Single(snippets);
        Assert.Equal("css", snippets[0].Language);
        Assert.False(snippets[0].CanApply);
    }
}
