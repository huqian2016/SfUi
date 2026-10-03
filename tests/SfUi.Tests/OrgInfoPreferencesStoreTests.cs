using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgInfoPreferencesStoreTests
{
    private static (OrgInfoPreferencesStore Store, string Sandbox) Create()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var paths = AppPaths.Resolve(dataRootOverride: sandbox, baseDirectory: sandbox, appDataDirectory: sandbox);
        var log = new AppLog(paths);
        return (new OrgInfoPreferencesStore(paths, log), sandbox);
    }

    private static void Cleanup(string sandbox)
    {
        try
        {
            Directory.Delete(sandbox, recursive: true);
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var (store, sandbox) = Create();
        try
        {
            var prefs = store.Load("00D1");

            Assert.Empty(prefs.Tabs);
        }
        finally
        {
            Cleanup(sandbox);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsAndIsolatesOrgs()
    {
        var (store, sandbox) = Create();
        try
        {
            store.Save("00D1", new OrgInfoOrgPreferences
            {
                Tabs =
                {
                    new OrgInfoCustomTab { Id = "tab1", Name = "Security", Items = { "overview:orgName", "stat:activeUsers" } },
                },
            });
            store.Save("00D2", new OrgInfoOrgPreferences
            {
                Tabs =
                {
                    new OrgInfoCustomTab { Id = "tab2", Name = "Ops", Items = { "settings:uiSkin" } },
                },
            });

            var org1 = store.Load("00D1");
            var org2 = store.Load("00D2");

            Assert.Single(org1.Tabs);
            Assert.Equal("Security", org1.Tabs[0].Name);
            Assert.Equal(new[] { "overview:orgName", "stat:activeUsers" }, org1.Tabs[0].Items);
            Assert.Single(org2.Tabs);
            Assert.Equal("Ops", org2.Tabs[0].Name);
        }
        finally
        {
            Cleanup(sandbox);
        }
    }

    [Fact]
    public void Load_DropsUnknownAndDuplicateItems()
    {
        var (store, sandbox) = Create();
        try
        {
            store.Save("00D1", new OrgInfoOrgPreferences
            {
                Tabs =
                {
                    new OrgInfoCustomTab
                    {
                        Id = "tab1",
                        Name = "Mixed",
                        Items = { "overview:orgName", "unknown:item", "overview:orgName", "stat:activeUsers" },
                    },
                },
            });

            var prefs = store.Load("00D1");

            Assert.Equal(new[] { "overview:orgName", "stat:activeUsers" }, prefs.Tabs[0].Items);
        }
        finally
        {
            Cleanup(sandbox);
        }
    }

    [Fact]
    public void Save_SkipsTabsWithoutId()
    {
        var (store, sandbox) = Create();
        try
        {
            var fired = 0;
            store.PreferencesUpdated += _ => fired++;
            store.Save("00D1", new OrgInfoOrgPreferences
            {
                Tabs =
                {
                    new OrgInfoCustomTab { Id = "", Name = "No id" },
                    new OrgInfoCustomTab { Id = "tab1", Name = "Keep" },
                },
            });

            var prefs = store.Load("00D1");

            Assert.Single(prefs.Tabs);
            Assert.Equal("Keep", prefs.Tabs[0].Name);
            Assert.Equal(1, fired);
        }
        finally
        {
            Cleanup(sandbox);
        }
    }
}
