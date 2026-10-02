using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class FavoritesStoreTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public FavoritesStoreTests()
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

    [Fact]
    public void Add_AssignsIdAndPersists()
    {
        var store = new FavoritesStore(_paths, _log);

        var item = store.Add(HistoryTypes.Soql, "全取引先", "SELECT Id, Name FROM Account");

        Assert.NotEmpty(item.Id);
        Assert.Equal(1, item.SortOrder);

        var reloaded = new FavoritesStore(_paths, _log);
        var loaded = Assert.Single(reloaded.GetAll());
        Assert.Equal("全取引先", loaded.Label);
        Assert.Equal("SELECT Id, Name FROM Account", loaded.Payload);
    }

    [Fact]
    public void GetByType_FiltersByType()
    {
        var store = new FavoritesStore(_paths, _log);
        store.Add(HistoryTypes.Soql, "SOQL 1", "SELECT Id FROM Account");
        store.Add(HistoryTypes.Apex, "Apex 1", "System.debug('x');");

        Assert.Single(store.GetByType(HistoryTypes.Soql));
        Assert.Single(store.GetByType(HistoryTypes.Apex));
        Assert.Empty(store.GetByType("url"));
    }

    [Fact]
    public void Update_ChangesLabelAndPayload()
    {
        var store = new FavoritesStore(_paths, _log);
        var item = store.Add(HistoryTypes.Command, "旧ラベル", "org list");

        store.Update(item.Id, label: "新ラベル", payload: "org display");

        var loaded = Assert.Single(store.GetAll());
        Assert.Equal("新ラベル", loaded.Label);
        Assert.Equal("org display", loaded.Payload);
    }

    [Fact]
    public void Remove_DeletesItem()
    {
        var store = new FavoritesStore(_paths, _log);
        var item = store.Add(HistoryTypes.Api, "limits", "/services/data/v67.0/limits");

        store.Remove(item.Id);

        Assert.Empty(store.GetAll());
        Assert.Null(store.Find(item.Id));
    }
}
