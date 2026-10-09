using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class HistoryStoreTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public HistoryStoreTests()
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

    private AppSettingsStore CreateSettings(long threshold = 64 * 1024, int maxPerType = 2000)
    {
        var settings = new AppSettingsStore(_paths, _log);
        settings.Current.ResultInlineThresholdBytes = threshold;
        settings.Current.MaxHistoryPerType = maxPerType;
        return settings;
    }

    private static DateTimeOffset At(int hour) => new(2026, 10, 2, hour, 0, 0, TimeSpan.FromHours(9));

    private static HistoryEntry Entry(string type, string? summary = null, DateTimeOffset? timestamp = null, string? org = null) => new()
    {
        Type = type,
        Summary = summary,
        Timestamp = timestamp ?? DateTimeOffset.Now,
        Org = org,
    };

    [Fact]
    public void Append_SetsDefaultsAndPersists()
    {
        var settings = CreateSettings();
        var store = new HistoryStore(_paths, _log, settings);

        var entry = new HistoryEntry
        {
            Type = HistoryTypes.Soql,
            Summary = "SELECT Id FROM Account",
            Params = "SELECT Id FROM Account",
        };
        store.Append(entry);

        Assert.NotEmpty(entry.Id);
        Assert.NotEqual(default, entry.Timestamp);
        Assert.True(File.Exists(Path.Combine(_sandbox, "history", "soql.json")));

        var reloaded = new HistoryStore(_paths, _log, settings);
        var loaded = Assert.Single(reloaded.Query());
        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal("SELECT Id FROM Account", loaded.Params);
    }

    [Fact]
    public void Query_FiltersAndSortsDescending()
    {
        var store = new HistoryStore(_paths, _log, CreateSettings());
        store.Append(Entry(HistoryTypes.Soql, "取引先一覧", At(9), org: "hks3"));
        store.Append(Entry(HistoryTypes.Soql, "商談一覧", At(10), org: "hks4"));
        store.Append(Entry(HistoryTypes.Apex, "アカウント更新", At(11), org: "hks3"));

        Assert.Equal(2, store.Query(new HistoryQuery(Type: HistoryTypes.Soql)).Count);
        Assert.Single(store.Query(new HistoryQuery(Org: "hks4")));
        Assert.Equal(2, store.Query(new HistoryQuery(SearchText: "hks3")).Count);

        var all = store.Query();
        Assert.Equal(3, all.Count);
        Assert.Equal(At(11), all[0].Timestamp); // 新しい順
    }

    [Fact]
    public void EtlEntry_IsStoredAndQueriedByType()
    {
        var store = new HistoryStore(_paths, _log, CreateSettings());
        store.Append(
            Entry(HistoryTypes.Etl, "step1: Contact", At(12), org: "hks4"),
            result: "step1: OK 2 / Failed 0 / Pending 0 / Skipped 0");

        var entry = Assert.Single(store.Query(new HistoryQuery(Type: HistoryTypes.Etl)));

        Assert.Equal(HistoryTypes.Etl, entry.Type);
        Assert.Equal("step1: Contact", entry.Summary);
        Assert.Equal("hks4", entry.Org);
        Assert.Equal("step1: OK 2 / Failed 0 / Pending 0 / Skipped 0", entry.ResultInline);
        Assert.Equal("ETL", entry.TypeLabel);
    }

    [Fact]
    public void Append_TrimsToMaxPerType()
    {
        var store = new HistoryStore(_paths, _log, CreateSettings(maxPerType: 2));
        store.Append(Entry(HistoryTypes.Soql, "A", At(9)));
        store.Append(Entry(HistoryTypes.Soql, "B", At(10)));
        store.Append(Entry(HistoryTypes.Soql, "C", At(11)));

        var entries = store.Query(new HistoryQuery(Type: HistoryTypes.Soql));

        Assert.Equal(2, entries.Count);
        Assert.DoesNotContain(entries, e => e.Summary == "A");
    }

    [Fact]
    public void Result_Small_IsStoredInline()
    {
        var store = new HistoryStore(_paths, _log, CreateSettings(threshold: 1024));
        var entry = Entry(HistoryTypes.Soql, "small");

        store.Append(entry, result: "{\"totalSize\":1}");

        Assert.Equal("{\"totalSize\":1}", entry.ResultInline);
        Assert.Null(entry.ResultRef);
        Assert.Equal("{\"totalSize\":1}", store.ReadResult(entry));
        Assert.Empty(Directory.GetFiles(_paths.ResultsDirectory));
    }

    [Fact]
    public void Result_Large_IsExternalized()
    {
        var settings = CreateSettings(threshold: 32);
        var store = new HistoryStore(_paths, _log, settings);
        var payload = new string('x', 200);
        var entry = Entry(HistoryTypes.Apex, "large");

        store.Append(entry, result: payload, resultFileExtension: "log");

        Assert.Null(entry.ResultInline);
        Assert.NotNull(entry.ResultRef);
        Assert.StartsWith("results", entry.ResultRef);
        Assert.True(File.Exists(Path.Combine(_sandbox, entry.ResultRef!)));
        Assert.Equal(payload, store.ReadResult(entry));

        // 再読み込みでも外部ファイル参照が維持される
        var reloaded = new HistoryStore(_paths, _log, settings);
        var loaded = Assert.Single(reloaded.Query(new HistoryQuery(Type: HistoryTypes.Apex)));
        Assert.Equal(payload, reloaded.ReadResult(loaded));
    }

    [Fact]
    public void Delete_RemovesEntryAndExternalResult()
    {
        var store = new HistoryStore(_paths, _log, CreateSettings(threshold: 32));
        var entry = Entry(HistoryTypes.Apex, "large");
        store.Append(entry, result: new string('y', 100), resultFileExtension: "log");
        var resultPath = Path.Combine(_sandbox, entry.ResultRef!);
        Assert.True(File.Exists(resultPath));

        store.Delete(entry.Type, entry.Id);

        Assert.Empty(store.Query());
        Assert.False(File.Exists(resultPath));
    }

    [Fact]
    public void Clear_RemovesAllTypes()
    {
        var settings = CreateSettings();
        var store = new HistoryStore(_paths, _log, settings);
        store.Append(Entry(HistoryTypes.Soql, "soql"));
        store.Append(Entry(HistoryTypes.Apex, "apex"));

        store.Clear();

        Assert.Empty(store.Query());
        var reloaded = new HistoryStore(_paths, _log, settings);
        Assert.Empty(reloaded.Query());
    }
}
