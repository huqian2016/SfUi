using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>エディタ系サブタブの永続化（editor-tabs.json）のテスト。</summary>
public class EditorTabsStoreTests : IDisposable
{
    private readonly string _sandbox;

    public EditorTabsStoreTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    private AppPaths Paths => AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
    private AppLog Log => new(Paths);

    [Fact]
    public void Load_MissingFile_ReturnsEmptyState()
    {
        var store = new EditorTabsStore(Paths, Log);

        var state = store.Load();

        Assert.Equal(1, state.Version);
        Assert.Null(state.Soql);
        Assert.Null(state.Apex);
        Assert.Null(state.Command);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsTabsAndSelection()
    {
        var store = new EditorTabsStore(Paths, Log);
        store.Save(new EditorTabsFile
        {
            Soql = new EditorTabHostState { Tabs = new() { "SELECT Id FROM Account", "SELECT Id FROM Contact" }, Selected = 1 },
            Apex = new EditorTabHostState { Tabs = new() { "System.debug('x');" }, Selected = 0 },
            Command = new EditorTabHostState { Tabs = new() { "org list", "sf version" }, Selected = 1 },
        });

        var reloaded = new EditorTabsStore(Paths, Log).Load();

        Assert.Equal(2, reloaded.Soql!.Tabs.Count);
        Assert.Equal("SELECT Id FROM Contact", reloaded.Soql.Tabs[1]);
        Assert.Equal(1, reloaded.Soql.Selected);
        Assert.Single(reloaded.Apex!.Tabs);
        Assert.Equal("System.debug('x');", reloaded.Apex.Tabs[0]);
        Assert.Equal(2, reloaded.Command!.Tabs.Count);
        Assert.Equal("sf version", reloaded.Command.Tabs[1]);
        Assert.Equal(1, reloaded.Command.Selected);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyState()
    {
        File.WriteAllText(Path.Combine(_sandbox, "editor-tabs.json"), "{ not valid json");

        var state = new EditorTabsStore(Paths, Log).Load();

        Assert.Null(state.Soql);
        Assert.Null(state.Command);
    }
}
