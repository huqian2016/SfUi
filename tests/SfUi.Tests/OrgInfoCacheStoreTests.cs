using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgInfoCacheStoreTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public OrgInfoCacheStoreTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
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

    private static OrgInfo DemoOrg() => new(
        Username: "user@example.com",
        Alias: "demo",
        OrgId: "00D000000000000AAA",
        InstanceUrl: "https://x.my.salesforce.com",
        ConnectedStatus: "Connected",
        IsDefault: true,
        IsSandbox: true);

    private static OrgInfoSection Section(string id, params (string Key, string? Value)[] cells)
    {
        var row = new OrgInfoRow { Id = "row1", Summary = "row1" };
        foreach (var (key, value) in cells)
        {
            row.Cells[key] = value;
        }

        return OrgInfoSection.Create(
            id,
            new[] { new OrgInfoColumn("value", "OrgInfo_Col_Value") },
            new[] { row },
            DateTimeOffset.Now,
            durationMs: 123);
    }

    [Fact]
    public void UpsertSection_PersistsAndReloads()
    {
        var store = new OrgInfoCacheStore(_paths, _log);
        var section = Section(OrgInfoSections.Overview, ("value", "HKS"));
        store.UpsertSection("00D000000000000AAA", section);

        var reloaded = new OrgInfoCacheStore(_paths, _log).GetSection("00D000000000000AAA", OrgInfoSections.Overview);

        Assert.NotNull(reloaded);
        Assert.Equal("HKS", reloaded!.Rows[0].Get("value"));
        Assert.Equal(section.FetchedAt, reloaded.FetchedAt);
        Assert.Equal(123, reloaded.DurationMs);
        Assert.True(File.Exists(store.GetCacheFilePath("00D000000000000AAA")));
    }

    [Fact]
    public void UpsertSection_ExistingSectionsAreKept()
    {
        var store = new OrgInfoCacheStore(_paths, _log);
        store.UpsertSection("k", Section(OrgInfoSections.Overview, ("value", "a")));
        store.UpsertSection("k", Section(OrgInfoSections.Users, ("value", "b")));

        var cache = store.Load("k");
        Assert.Equal(2, cache.Sections.Count);
        Assert.Equal("b", cache.Sections[OrgInfoSections.Users].Rows[0].Get("value"));
    }

    [Fact]
    public void UpsertSection_FiresSectionUpdated()
    {
        var store = new OrgInfoCacheStore(_paths, _log);
        var raised = new List<(string OrgKey, string SectionId)>();
        store.SectionUpdated += (orgKey, sectionId) => raised.Add((orgKey, sectionId));

        store.UpsertSection("org-key", Section(OrgInfoSections.Users));

        var raisedEvent = Assert.Single(raised);
        Assert.Equal("org-key", raisedEvent.OrgKey);
        Assert.Equal(OrgInfoSections.Users, raisedEvent.SectionId);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyCache()
    {
        var cache = new OrgInfoCacheStore(_paths, _log).Load("unknown");

        Assert.Empty(cache.Sections);
        Assert.Equal(OrgInfoCacheStore.CurrentSchemaVersion, cache.SchemaVersion);
    }

    [Fact]
    public void Load_OldSchema_DiscardsSectionsAndKeepsOrgMetadata()
    {
        var store = new OrgInfoCacheStore(_paths, _log);
        var cache = new OrgInfoCache
        {
            SchemaVersion = 0,
            Org = new OrgInfoCacheOrg { Username = "user@example.com" },
            Sections = { [OrgInfoSections.Users] = Section(OrgInfoSections.Users) },
        };
        AtomicJsonFile.Save(store.GetCacheFilePath("old"), cache, _log);

        var loaded = store.Load("old");

        Assert.Empty(loaded.Sections);
        Assert.Equal("user@example.com", loaded.Org!.Username);
    }

    [Fact]
    public void GetOrgKey_PrefersOrgIdThenUsername()
    {
        Assert.Equal("00D000000000000AAA", OrgInfoCacheStore.GetOrgKey(DemoOrg()));
        Assert.Equal("user@example.com", OrgInfoCacheStore.GetOrgKey(DemoOrg() with { OrgId = null }));
        Assert.Equal("org", OrgInfoCacheStore.GetOrgKey(DemoOrg() with { OrgId = null, Username = "  " }));
    }

    [Fact]
    public void SanitizeOrgKey_ReplacesInvalidFileNameChars()
    {
        var sanitized = OrgInfoCacheStore.SanitizeOrgKey("a/b:c*d?e");

        Assert.DoesNotContain('/', sanitized);
        Assert.DoesNotContain(':', sanitized);
        Assert.DoesNotContain('*', sanitized);
        Assert.DoesNotContain('?', sanitized);
        Assert.Equal("org", OrgInfoCacheStore.SanitizeOrgKey("   "));
    }

    [Fact]
    public void UpdateOrgMetadata_StoresOrgInfo()
    {
        var store = new OrgInfoCacheStore(_paths, _log);
        store.UpdateOrgMetadata("demo", DemoOrg());

        var cache = store.Load("demo");

        Assert.Equal("user@example.com", cache.Org!.Username);
        Assert.Equal("00D000000000000AAA", cache.Org.OrgId);
        Assert.True(cache.Org.IsSandbox);
    }
}
