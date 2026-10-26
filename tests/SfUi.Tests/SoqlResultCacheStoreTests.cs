using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>SOQL 結果キャッシュ（data/soql-cache）の保存・検索・復元。</summary>
public class SoqlResultCacheStoreTests : IDisposable
{
    private const string RecordsJson =
        "[{\"attributes\":{\"type\":\"Account\"},\"Id\":\"001A\",\"Name\":\"Acme\"}," +
        "{\"attributes\":{\"type\":\"Account\"},\"Id\":\"001B\",\"Name\":\"Globex\"}]";

    private readonly string _sandbox;

    public SoqlResultCacheStoreTests()
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

    private SoqlResultCacheStore CreateStore() => new(Paths, new AppLog(Paths));

    private static SoqlCacheEntry CreateEntry(string query = "select id, name from account limit 10", bool useToolingApi = false) => new()
    {
        Query = SoqlResultCacheStore.NormalizeQuery(query),
        DisplayQuery = query,
        UseToolingApi = useToolingApi,
        Engine = "REST",
        TotalSize = 2,
        Done = true,
        RowCount = 2,
        RawJson = RecordsJson,
    };

    [Fact]
    public void SaveAndFind_RoundTripsEntry()
    {
        var store = CreateStore();
        store.Save("dev@example.com", CreateEntry());

        var found = CreateStore().Find("dev@example.com", "SELECT Id, Name FROM Account LIMIT 10");

        Assert.NotNull(found);
        Assert.Equal(2, found!.RowCount);
        Assert.Equal("REST", found.Engine);
        Assert.Equal(RecordsJson, found.RawJson);
    }

    [Fact]
    public void NormalizeQuery_IgnoresCaseAndWhitespace()
    {
        Assert.Equal("select id from account", SoqlResultCacheStore.NormalizeQuery("  SELECT   Id\nFROM\tAccount  "));
        Assert.Equal(string.Empty, SoqlResultCacheStore.NormalizeQuery("   "));
    }

    [Fact]
    public void Find_ReturnsNull_ForDifferentOrgOrToolingFlag()
    {
        var store = CreateStore();
        store.Save("dev@example.com", CreateEntry());

        Assert.Null(store.Find("other@example.com", "select id, name from account limit 10"));
        Assert.Null(store.Find("dev@example.com", "select id, name from account limit 10", useToolingApi: true));
        Assert.Null(store.Find("dev@example.com", "select id from contact"));
    }

    [Fact]
    public void Save_KeepsOnlyTheLatestEntriesPerOrg()
    {
        var store = CreateStore();
        for (var i = 0; i < SoqlResultCacheStore.MaxEntriesPerOrg + 5; i++)
        {
            store.Save("dev@example.com", CreateEntry($"select id from account where name = 'n{i}'"));
        }

        var file = File.ReadAllText(store.FilePathFor("dev@example.com"));
        var count = System.Text.RegularExpressions.Regex.Matches(file, "\"query\"").Count;
        Assert.Equal(SoqlResultCacheStore.MaxEntriesPerOrg, count);
        Assert.NotNull(store.Find("dev@example.com", $"select id from account where name = 'n{SoqlResultCacheStore.MaxEntriesPerOrg + 4}'"));
        Assert.Null(store.Find("dev@example.com", "select id from account where name = 'n0'"));
    }

    [Fact]
    public void Save_OverwritesExistingQuery()
    {
        var store = CreateStore();
        store.Save("dev@example.com", CreateEntry());

        var updated = CreateEntry();
        updated.RowCount = 5;
        updated.CachedAt = DateTimeOffset.Now.AddMinutes(5);
        store.Save("dev@example.com", updated);

        var file = File.ReadAllText(store.FilePathFor("dev@example.com"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(file, "\"query\""));
        Assert.Equal(5, store.Find("dev@example.com", "select id, name from account limit 10")!.RowCount);
    }

    [Fact]
    public void Remove_DropsOnlyTheGivenQuery()
    {
        var store = CreateStore();
        store.Save("dev@example.com", CreateEntry());
        store.Save("dev@example.com", CreateEntry("select id from contact"));

        store.Remove("dev@example.com", "SELECT Id, Name FROM Account LIMIT 10");

        Assert.Null(store.Find("dev@example.com", "select id, name from account limit 10"));
        Assert.NotNull(store.Find("dev@example.com", "select id from contact"));
    }

    [Fact]
    public void ToResult_RebuildsTheSameTable()
    {
        var result = SoqlResultCacheStore.ToResult(CreateEntry());

        Assert.NotNull(result);
        Assert.Equal(2, result!.Table.Rows.Count);
        Assert.Contains("Id", result.Table.Columns.Cast<System.Data.DataColumn>().Select(column => column.ColumnName));
        Assert.Contains("Name", result.Table.Columns.Cast<System.Data.DataColumn>().Select(column => column.ColumnName));
        Assert.Equal("Acme", result.Table.Rows[0]["Name"]);
    }

    [Fact]
    public void ToResult_ReturnsNull_ForBrokenJson()
    {
        var entry = CreateEntry();
        entry.RawJson = "{ not json";

        Assert.Null(SoqlResultCacheStore.ToResult(entry));
    }

    [Fact]
    public void Load_BrokenFile_ReturnsNoEntries()
    {
        var store = CreateStore();
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePathFor("dev@example.com"))!);
        File.WriteAllText(store.FilePathFor("dev@example.com"), "{ broken");

        Assert.Null(CreateStore().Find("dev@example.com", "select id from account"));
    }

    [Fact]
    public void Clear_RemovesAllEntriesForOrg()
    {
        var store = CreateStore();
        store.Save("dev@example.com", CreateEntry());

        store.Clear("dev@example.com");

        Assert.Null(store.Find("dev@example.com", "select id, name from account limit 10"));
    }
}
