using SfUi.App.ViewModels;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>エディタ系サブタブ ホスト（SOQL / 匿名Apex / コマンドの複数タブ）のテスト。</summary>
[Collection("Localization")]
public class EditorTabHostTests
{
    private sealed class DummyTab
    {
        public string Title { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;
    }

    private static EditorTabHost<DummyTab> CreateHost(string labelKey = "Tab_Soql") =>
        new(() => new DummyTab(), (tab, title) => tab.Title = title, labelKey, tab => tab.Text, (tab, text) => tab.Text = text);

    [Fact]
    public void AddTab_AddsAndSelectsNewTab()
    {
        var host = CreateHost();

        var first = host.AddTab();
        Assert.Single(host.Tabs);
        Assert.Same(first, host.SelectedTab);

        var second = host.AddTab();
        Assert.Equal(2, host.Tabs.Count);
        Assert.Same(second, host.SelectedTab);
    }

    [Fact]
    public void AddTab_AssignsPositionalTitles()
    {
        var host = CreateHost();

        var first = host.AddTab();
        var second = host.AddTab();

        Assert.Equal(UiText.T("Main_EditorTabTitleFmt", UiText.T("Tab_Soql"), 1), first.Title);
        Assert.Equal(UiText.T("Main_EditorTabTitleFmt", UiText.T("Tab_Soql"), 2), second.Title);
    }

    [Fact]
    public void AddNewTabCommand_AddsAndSelects()
    {
        var host = CreateHost();
        host.AddTab();

        host.AddNewTabCommand.Execute(null);

        Assert.Equal(2, host.Tabs.Count);
        Assert.Same(host.Tabs[1], host.SelectedTab);
    }

    [Fact]
    public void CloseTab_RemovesAndRenumbersTitles()
    {
        var host = CreateHost();
        var first = host.AddTab();
        var second = host.AddTab();
        var third = host.AddTab();

        host.CloseTabCommand.Execute(second);

        Assert.Equal(new[] { first, third }, host.Tabs);
        Assert.Equal(UiText.T("Main_EditorTabTitleFmt", UiText.T("Tab_Soql"), 1), first.Title);
        Assert.Equal(UiText.T("Main_EditorTabTitleFmt", UiText.T("Tab_Soql"), 2), third.Title);
    }

    [Fact]
    public void CloseTab_SelectedTab_SelectsNeighborAtSameIndex()
    {
        var host = CreateHost();
        host.AddTab();
        var second = host.AddTab();
        var third = host.AddTab();
        host.SelectedTab = second;

        host.CloseTabCommand.Execute(second);
        Assert.Same(third, host.SelectedTab);

        host.CloseTabCommand.Execute(third);
        Assert.Single(host.Tabs);
        Assert.Same(host.Tabs[0], host.SelectedTab);
    }

    [Fact]
    public void CloseTab_LastTab_CannotExecute()
    {
        var host = CreateHost();
        var only = host.AddTab();

        Assert.False(host.CloseTabCommand.CanExecute(only));

        var second = host.AddTab();
        Assert.True(host.CloseTabCommand.CanExecute(only));

        host.CloseTabCommand.Execute(second);

        Assert.Single(host.Tabs);
        Assert.False(host.CloseTabCommand.CanExecute(only));
    }

    [Fact]
    public void RefreshTitles_UsesCurrentLanguage()
    {
        var original = UiText.Language;
        try
        {
            var host = CreateHost("Tab_Apex");

            UiText.SetLanguage(UiText.Japanese);
            var tab = host.AddTab();
            Assert.StartsWith("匿名Apex", tab.Title);

            UiText.SetLanguage(UiText.English);
            host.RefreshTitles();
            Assert.StartsWith("Apex", tab.Title);
        }
        finally
        {
            UiText.SetLanguage(original);
        }
    }

    [Fact]
    public void ExportTexts_ReturnsTextsInOrder()
    {
        var host = CreateHost();
        host.AddTab().Text = "SELECT 1";
        host.AddTab().Text = "SELECT 2";

        Assert.Equal(new[] { "SELECT 1", "SELECT 2" }, host.ExportTexts());
    }

    [Fact]
    public void RestoreFromTexts_RebuildsTabsAndSelection()
    {
        var host = CreateHost();

        host.RestoreFromTexts(new[] { "A", "B", "C" }, 2);

        Assert.Equal(3, host.Tabs.Count);
        Assert.Equal(new[] { "A", "B", "C" }, host.Tabs.Select(t => t.Text));
        Assert.Equal(2, host.SelectedIndex);
        Assert.Same(host.Tabs[2], host.SelectedTab);
        Assert.Equal(UiText.T("Main_EditorTabTitleFmt", UiText.T("Tab_Soql"), 3), host.SelectedTab!.Title);
    }

    [Fact]
    public void RestoreFromTexts_ClampsSelection()
    {
        var host = CreateHost();

        host.RestoreFromTexts(new[] { "A" }, 5);

        Assert.Equal(0, host.SelectedIndex);
    }

    [Fact]
    public void Changed_FiresOnAddCloseAndSelect()
    {
        var host = CreateHost();
        var count = 0;
        host.Changed += () => count++;

        var first = host.AddTab();
        var second = host.AddTab();
        host.SelectedTab = first;
        host.CloseTabCommand.Execute(second);

        Assert.True(count >= 4, $"Changed fired {count} times");
    }

    // ---- タブ名の変更・複製・永続化 ----

    private static EditorTabHost<DummyTab> CreateRenamableHost(Func<DummyTab, string?> prompt) =>
        new(() => new DummyTab(), (tab, title) => tab.Title = title, "Tab_Soql",
            tab => tab.Text, (tab, text) => tab.Text = text, tab => tab.Title)
        {
            TitlePrompt = prompt,
        };

    [Fact]
    public void RenameTab_SetsCustomTitleAndKeepsItAfterAddingTabs()
    {
        var host = CreateRenamableHost(_ => "My query");
        var first = host.AddTab();

        host.RenameTabCommand.Execute(first);

        Assert.Equal("My query", first.Title);
        Assert.True(host.HasCustomTitle(first));

        var second = host.AddTab();
        Assert.Equal("My query", first.Title);
        Assert.Equal(UiText.T("Main_EditorTabTitleFmt", UiText.T("Tab_Soql"), 2), second.Title);
    }

    [Fact]
    public void RenameTab_EmptyInput_RestoresPositionalTitle()
    {
        var host = CreateRenamableHost(_ => "Temp");
        var first = host.AddTab();
        host.RenameTab(first);

        host.TitlePrompt = _ => "   ";
        host.RenameTab(first);

        Assert.False(host.HasCustomTitle(first));
        Assert.Equal(UiText.T("Main_EditorTabTitleFmt", UiText.T("Tab_Soql"), 1), first.Title);
    }

    [Fact]
    public void RenameTab_NullPromptResultOrMissingProvider_DoesNothing()
    {
        var host = CreateRenamableHost(_ => null);
        var first = host.AddTab();
        var before = first.Title;

        host.RenameTab(first);
        Assert.Equal(before, first.Title);

        var noPrompt = CreateHost();
        var tab = noPrompt.AddTab();
        var defaultTitle = tab.Title;
        noPrompt.RenameTab(tab);
        Assert.Equal(defaultTitle, tab.Title);
    }

    [Fact]
    public void DuplicateTab_CopiesTextAndInsertsAfterSource()
    {
        var host = CreateRenamableHost(_ => null);
        host.AddTab();
        var source = host.AddTab();
        source.Text = "SELECT Id FROM Account";
        var last = host.AddTab();
        var sourceIndex = host.Tabs.IndexOf(source);

        var copy = host.DuplicateTab(source);

        Assert.NotNull(copy);
        Assert.Equal(4, host.Tabs.Count);
        Assert.Same(copy, host.Tabs[sourceIndex + 1]);
        Assert.Same(last, host.Tabs[sourceIndex + 2]);
        Assert.Equal("SELECT Id FROM Account", copy!.Text);
        Assert.Same(copy, host.SelectedTab);
    }

    [Fact]
    public void DuplicateTab_CustomTitle_GetsCopySuffix()
    {
        var host = CreateRenamableHost(_ => "Reports");
        var source = host.AddTab();
        host.RenameTab(source);

        var copy = host.DuplicateTab(source);

        Assert.NotNull(copy);
        Assert.Equal(UiText.T("Main_EditorTabCopyFmt", "Reports"), copy!.Title);
        Assert.True(host.HasCustomTitle(copy));
    }

    [Fact]
    public void DuplicateTab_DefaultTitle_UsesPositionalNumbering()
    {
        var host = CreateRenamableHost(_ => null);
        var source = host.AddTab();

        var copy = host.DuplicateTab(source);

        Assert.NotNull(copy);
        Assert.False(host.HasCustomTitle(copy!));
        Assert.Equal(UiText.T("Main_EditorTabTitleFmt", UiText.T("Tab_Soql"), 2), copy.Title);
    }

    [Fact]
    public void DuplicateTab_UnknownTab_ReturnsNull()
    {
        var host = CreateHost();
        host.AddTab();

        Assert.Null(host.DuplicateTab(new DummyTab()));
    }

    [Fact]
    public void ExportTitles_RoundTripsThroughRestoreTitles()
    {
        var host = CreateRenamableHost(_ => null);
        host.AddTab();
        var second = host.AddTab();
        host.TitlePrompt = _ => "Named";
        host.RenameTab(second);

        var titles = host.ExportTitles();

        Assert.Equal(2, titles.Count);
        Assert.Null(titles[0]);
        Assert.Equal("Named", titles[1]);

        var restored = CreateRenamableHost(_ => null);
        restored.AddTab();
        restored.AddTab();
        restored.RestoreTitles(titles);

        Assert.False(restored.HasCustomTitle(restored.Tabs[0]));
        Assert.True(restored.HasCustomTitle(restored.Tabs[1]));
        Assert.Equal("Named", restored.Tabs[1].Title);
    }

    [Fact]
    public void RestoreFromTexts_ClearsCustomTitles()
    {
        var host = CreateRenamableHost(_ => "Named");
        var first = host.AddTab();
        host.RenameTab(first);

        host.RestoreFromTexts(new[] { "SELECT Id FROM Account", "SELECT Id FROM Contact" }, 1);

        Assert.Equal(2, host.Tabs.Count);
        Assert.False(host.HasCustomTitle(host.Tabs[0]));
        Assert.Equal("SELECT Id FROM Contact", host.Tabs[1].Text);
        Assert.Same(host.Tabs[1], host.SelectedTab);
    }
}
