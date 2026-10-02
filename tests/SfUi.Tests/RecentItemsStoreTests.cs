using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class RecentItemsStoreTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public RecentItemsStoreTests()
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

    private static DateTimeOffset At(int hour) => new(2026, 10, 2, hour, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void Touch_AddsNewItem()
    {
        var store = new RecentFoldersStore(_paths, _log);

        var item = store.Touch(@"C:\work\A", At(9));

        var ordered = store.GetOrdered();
        Assert.Single(ordered);
        Assert.Equal(@"C:\work\A", ordered[0].Path);
        Assert.Equal(1, item.UseCount);
        Assert.Equal(At(9), item.LastUsedAt);
    }

    [Fact]
    public void Touch_ExistingItem_IncrementsCountAndUpdatesTimestamp()
    {
        var store = new RecentFoldersStore(_paths, _log);
        store.Touch(@"C:\work\A", At(9));

        store.Touch(@"C:\work\A", At(11));

        var item = Assert.Single(store.GetOrdered());
        Assert.Equal(2, item.UseCount);
        Assert.Equal(At(11), item.LastUsedAt);
    }

    [Fact]
    public void GetOrdered_PinnedFirst_ThenByLastUsedDescending()
    {
        var store = new RecentFoldersStore(_paths, _log);
        store.Touch(@"C:\work\A", At(9));
        store.Touch(@"C:\work\B", At(10));
        store.Touch(@"C:\work\C", At(11));
        store.SetPinned(@"C:\work\A", true);

        var ordered = store.GetOrdered();

        Assert.Equal(new[] { @"C:\work\A", @"C:\work\C", @"C:\work\B" }, ordered.Select(i => i.Path).ToArray());
    }

    [Fact]
    public void MaxItems_TrimsOldestNonPinnedItems()
    {
        var filePath = Path.Combine(_sandbox, "recent-custom.json");
        var store = new RecentItemsStore<RecentFolder>(filePath, _log, maxItems: 3);
        store.Touch(@"C:\A", At(9));
        store.Touch(@"C:\B", At(10));
        store.Touch(@"C:\C", At(11));
        store.Touch(@"C:\D", At(12)); // A が上限超過で削除される
        store.SetPinned(@"C:\D", true);
        store.Touch(@"C:\E", At(13)); // B が削除される（ピン留めの D は残る）

        var keys = store.GetOrdered().Select(i => i.Path).ToList();

        Assert.Equal(3, keys.Count);
        Assert.DoesNotContain(@"C:\A", keys);
        Assert.DoesNotContain(@"C:\B", keys);
        Assert.Contains(@"C:\D", keys);
    }

    [Fact]
    public void Remove_And_Clear()
    {
        var store = new RecentFoldersStore(_paths, _log);
        store.Touch(@"C:\A", At(9));
        store.Touch(@"C:\B", At(10));

        store.Remove(@"C:\A");
        Assert.Single(store.GetOrdered());

        store.Clear();
        Assert.Empty(store.GetOrdered());
    }

    [Fact]
    public void Persistence_RoundTrips()
    {
        var store = new RecentFoldersStore(_paths, _log);
        store.Touch(@"C:\work\A", At(9));
        store.SetPinned(@"C:\work\A", true);

        var reloaded = new RecentFoldersStore(_paths, _log);

        var item = Assert.Single(reloaded.GetOrdered());
        Assert.Equal(@"C:\work\A", item.Path);
        Assert.True(item.IsPinned);
        Assert.Equal(1, item.UseCount);
    }

    [Fact]
    public void RecentUrlsStore_UsesSeparateFile()
    {
        var store = new RecentUrlsStore(_paths, _log);

        store.Touch("https://hks3.my.salesforce.com", At(9));

        Assert.True(File.Exists(Path.Combine(_sandbox, "recent-urls.json")));
        Assert.Single(store.GetOrdered());
    }
}
