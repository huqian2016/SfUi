using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgCompareStateStoreTests : IDisposable
{
    private readonly string _sandbox;

    public OrgCompareStateStoreTests()
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
    private OrgCompareStateStore Store => new(Paths, Log);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var state = Store.Load();

        Assert.Equal(OrgCompareStateStore.CurrentSchemaVersion, state.SchemaVersion);
        Assert.Empty(state.OrgUsernames);
        Assert.Null(state.CategoryId);
        Assert.False(state.DiffOnly);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsValues()
    {
        var store = Store;
        store.Save(new OrgCompareState
        {
            OrgUsernames = new List<string> { "user1@example.com", "user2@example.com" },
            CategoryId = OrgInfoSections.PermissionSets,
            DiffOnly = true,
        });

        var reloaded = Store.Load();

        Assert.Equal(new[] { "user1@example.com", "user2@example.com" }, reloaded.OrgUsernames);
        Assert.Equal(OrgInfoSections.PermissionSets, reloaded.CategoryId);
        Assert.True(reloaded.DiffOnly);
    }

    [Fact]
    public void Save_ClampsToMaxOrgs_AndDeduplicates()
    {
        var store = Store;
        store.Save(new OrgCompareState
        {
            OrgUsernames = new List<string> { "a@x.com", " b@x.com ", "a@X.com", "c@x.com", "d@x.com", "e@x.com", "f@x.com", "g@x.com", "h@x.com", "i@x.com" },
        });

        var reloaded = Store.Load();

        Assert.Equal(OrgCompareStateStore.MaxOrgs, reloaded.OrgUsernames.Count);
        Assert.Equal(new[] { "a@x.com", "b@x.com", "c@x.com", "d@x.com", "e@x.com", "f@x.com", "g@x.com", "h@x.com" }, reloaded.OrgUsernames);
    }

    [Fact]
    public void Save_roundtrips_fields_object()
    {
        Store.Save(new OrgCompareState { FieldsObject = " Account " });
        Assert.Equal("Account", Store.Load().FieldsObject);

        Store.Save(new OrgCompareState());
        Assert.Null(Store.Load().FieldsObject);
    }

    [Fact]
    public void Save_keeps_dynamic_fields_category_id()
    {
        Store.Save(new OrgCompareState { CategoryId = "fields:Account" });
        Assert.Equal("fields:Account", Store.Load().CategoryId);

        Store.Save(new OrgCompareState { CategoryId = "records:Account" });
        Assert.Equal("records:Account", Store.Load().CategoryId);

        Store.Save(new OrgCompareState { CategoryId = "fields:" });
        Assert.Null(Store.Load().CategoryId);
    }

    [Fact]
    public void Save_roundtrips_record_compare_settings()
    {
        Store.Save(new OrgCompareState
        {
            RecordObject = " Account ",
            RecordKeyField = " Name ",
            RecordFields = new List<string> { "Name", " Industry ", "name", "  " },
            RecordLimit = 150,
        });

        var state = Store.Load();
        Assert.Equal("Account", state.RecordObject);
        Assert.Equal("Name", state.RecordKeyField);
        Assert.Equal(new[] { "Name", "Industry" }, state.RecordFields);
        Assert.Equal(150, state.RecordLimit);

        Store.Save(new OrgCompareState { RecordLimit = 999999 });
        Assert.Equal(OrgRecordCompareService.MaxLimit, Store.Load().RecordLimit);
    }

    [Fact]
    public void Save_ResetsUnknownCategory()
    {
        var store = Store;
        store.Save(new OrgCompareState { CategoryId = "nope" });

        Assert.Null(Store.Load().CategoryId);
    }

    [Fact]
    public void Load_SchemaMismatch_ResetsToDefaults()
    {
        var store = Store;
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """
            {
              "schemaVersion": 0,
              "orgUsernames": ["a@x.com"],
              "categoryId": "users",
              "diffOnly": true
            }
            """);

        var loaded = store.Load();

        Assert.Equal(OrgCompareStateStore.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Empty(loaded.OrgUsernames);
        Assert.Null(loaded.CategoryId);
        Assert.False(loaded.DiffOnly);
    }
}
