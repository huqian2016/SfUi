using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>組織一覧キャッシュ（orgs-cache.json）のテスト。</summary>
public class OrgCacheStoreTests : IDisposable
{
    private readonly string _sandbox;

    public OrgCacheStoreTests()
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
    public void Load_MissingFile_ReturnsEmpty()
    {
        var store = new OrgCacheStore(Paths, Log);

        var cache = store.Load();

        Assert.Empty(cache.Orgs);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsOrgs()
    {
        var orgs = new[]
        {
            new OrgInfo("dev@acme.example.com", "dev", "00DXX", "https://acme.my.salesforce.com", "Connected", true, false),
            new OrgInfo("qa@acme.example.com", null, "00DYY", "https://qa.my.salesforce.com", null, false, true),
        };

        new OrgCacheStore(Paths, Log).Save(orgs);

        var cache = new OrgCacheStore(Paths, Log).Load();

        Assert.Equal(2, cache.Orgs.Count);
        var first = cache.Orgs[0];
        Assert.Equal("dev@acme.example.com", first.Username);
        Assert.Equal("dev", first.Alias);
        Assert.Equal("00DXX", first.OrgId);
        Assert.Equal("https://acme.my.salesforce.com", first.InstanceUrl);
        Assert.Equal("Connected", first.ConnectedStatus);
        Assert.True(first.IsDefault);
        Assert.False(first.IsSandbox);
        Assert.Equal("dev (dev@acme.example.com)", first.DisplayName);
        Assert.Null(cache.Orgs[1].Alias);
        Assert.True(cache.Orgs[1].IsSandbox);
        Assert.True(cache.SavedAt > DateTimeOffset.Now.AddMinutes(-1));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty()
    {
        File.WriteAllText(Path.Combine(_sandbox, "orgs-cache.json"), "{ not valid json");

        var cache = new OrgCacheStore(Paths, Log).Load();

        Assert.Empty(cache.Orgs);
    }
}
