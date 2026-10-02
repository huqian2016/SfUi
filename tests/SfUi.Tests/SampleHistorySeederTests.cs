using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class SampleHistorySeederTests : IDisposable
{
    private readonly string _sandbox;

    public SampleHistorySeederTests()
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

    private HistoryStore CreateHistory()
    {
        var paths = Paths;
        return new HistoryStore(paths, Log, new AppSettingsStore(paths, Log));
    }

    [Fact]
    public void Seed_AddsRepresentativeSamples_ForAllTypes()
    {
        var history = CreateHistory();

        var result = SampleHistorySeeder.Seed(history, Log);

        Assert.Equal(30, result.Added);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(9, history.Query(new HistoryQuery(Type: HistoryTypes.Soql)).Count);
        Assert.Equal(6, history.Query(new HistoryQuery(Type: HistoryTypes.Apex)).Count);
        Assert.Equal(8, history.Query(new HistoryQuery(Type: HistoryTypes.Command)).Count);
        Assert.Equal(7, history.Query(new HistoryQuery(Type: HistoryTypes.Api)).Count);
        Assert.All(history.Query(), entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Params));
            Assert.False(string.IsNullOrWhiteSpace(entry.Summary));
            Assert.Equal("success", entry.Status);
        });
    }

    [Fact]
    public void Seed_CalledTwice_SkipsDuplicates()
    {
        var history = CreateHistory();

        var first = SampleHistorySeeder.Seed(history, Log);
        var second = SampleHistorySeeder.Seed(history, Log);

        Assert.Equal(0, second.Added);
        Assert.Equal(first.Added, second.Skipped);
        Assert.Equal(first.Added, history.Query().Count);
    }

    [Fact]
    public void Seed_KeepsExistingEntries()
    {
        var history = CreateHistory();
        history.Append(new HistoryEntry
        {
            Type = HistoryTypes.Soql,
            Params = "SELECT Id FROM Account LIMIT 1",
            Summary = "既存エントリ",
        });

        SampleHistorySeeder.Seed(history, Log);

        var entries = history.Query(new HistoryQuery(Type: HistoryTypes.Soql));
        Assert.Contains(entries, e => e.Summary == "既存エントリ");
        Assert.Equal(10, entries.Count); // 既存 1 件 + サンプル 9 件
    }
}
