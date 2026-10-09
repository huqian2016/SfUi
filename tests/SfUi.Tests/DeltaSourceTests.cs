using SfUi.Etl.Engine;
using SfUi.Etl.Sources;
using Xunit;

namespace SfUi.Tests;

public class DeltaSourceTests
{
    [Fact]
    public void NoWatermark_PassesAllRows_AndTracksMax()
    {
        var inner = new FakeSource(
            new[] { "Id", "ModifiedAt", "Name" },
            new object?[] { "1", "2026-01-01 00:00:00", "Alpha" },
            new object?[] { "2", "2026-01-03 12:00:00", "Beta" });
        var source = new DeltaSource(inner, "ModifiedAt", null);

        Assert.Equal(2, source.Count);
        Assert.Equal(2, source.ReadRows().Count());
        Assert.Equal(DateTimeOffset.Parse("2026-01-03T12:00:00Z"), source.MaxValue);
        Assert.Null(source.Watermark);
    }

    [Fact]
    public void Watermark_ExcludesOlderAndEqualRows()
    {
        var inner = new FakeSource(
            new[] { "Id", "ModifiedAt" },
            new object?[] { "1", "2026-01-01 00:00:00" },
            new object?[] { "2", "2026-01-02 00:00:00" },
            new object?[] { "3", "2026-01-03 00:00:00" });
        var source = new DeltaSource(inner, "ModifiedAt", DateTimeOffset.Parse("2026-01-02T00:00:00Z"));

        var rows = source.ReadRows().ToList();
        Assert.Single(rows);
        Assert.Equal("3", rows[0][0]);
        Assert.Equal(DateTimeOffset.Parse("2026-01-03T00:00:00Z"), source.MaxValue);
    }

    [Fact]
    public void NullAndUnparseable_KeptButNotCountedInMax()
    {
        var inner = new FakeSource(
            new[] { "Id", "ModifiedAt" },
            new object?[] { "1", null },
            new object?[] { "2", "not-a-date" },
            new object?[] { "3", "2026-01-05 00:00:00" });
        var source = new DeltaSource(inner, "ModifiedAt", null);

        Assert.Equal(3, source.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-01-05T00:00:00Z"), source.MaxValue);
    }

    [Fact]
    public void MissingColumn_Throws()
    {
        var inner = new FakeSource(new[] { "Id" }, new object?[] { "1" });
        var ex = Assert.Throws<ArgumentException>(() => new DeltaSource(inner, "Missing", null));
        Assert.Contains("Missing", ex.Message);
    }

    [Fact]
    public void ColumnLookup_IsCaseInsensitive()
    {
        var inner = new FakeSource(
            new[] { "Id", "ModifiedAt" },
            new object?[] { "1", "2026-01-01T00:00:00Z" });
        var source = new DeltaSource(inner, "modifiedat", null);

        Assert.Equal(1, source.Count);
    }

    [Fact]
    public void TimestampFormats_AreParsed()
    {
        Assert.True(DeltaSource.TryParseTimestamp("2026-01-02T03:04:05Z", out var iso));
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), iso);

        Assert.True(DeltaSource.TryParseTimestamp("2026-01-02T03:04:05.000+0000", out var soql));
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), soql);

        Assert.True(DeltaSource.TryParseTimestamp("2026-01-02 03:04:05", out var space));
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), space);

        Assert.True(DeltaSource.TryParseTimestamp(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified), out var typed));
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), typed);

        Assert.True(DeltaSource.TryParseTimestamp(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(9)), out var offset));
        Assert.Equal(9, offset.Offset.TotalHours);

        Assert.False(DeltaSource.TryParseTimestamp("", out _));
        Assert.False(DeltaSource.TryParseTimestamp("garbage", out _));
    }

    [Fact]
    public void NameAndColumns_DelegateToInner()
    {
        var inner = new FakeSource(new[] { "A" }, new object?[] { "x" });
        var source = new DeltaSource(inner, "A", null);

        Assert.Equal(inner.Name, source.Name);
        Assert.Equal(inner.Columns, source.Columns);
    }

    private sealed class FakeSource : IEtlSource
    {
        private readonly object?[][] _rows;

        public FakeSource(string[] columns, params object?[][] rows)
        {
            Columns = columns;
            _rows = rows;
        }

        public string Name => "fake";

        public IReadOnlyList<string> Columns { get; }

        public IEnumerable<object?[]> ReadRows() => _rows;
    }
}

public class DeltaStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-delta-" + Guid.NewGuid().ToString("N"));

    public DeltaStateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void SetGet_RoundTrip_MergesStepIds()
    {
        var path = DeltaState.FilePath(_dir, "job-a");
        var t1 = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

        DeltaState.SetWatermark(path, "step1", t1);
        DeltaState.SetWatermark(path, "step2", t2);

        Assert.Equal(t1, DeltaState.GetWatermark(path, "step1"));
        Assert.Equal(t2, DeltaState.GetWatermark(path, "step2"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Get_MissingFile_ReturnsNull()
    {
        var path = DeltaState.FilePath(_dir, "none");
        Assert.Null(DeltaState.GetWatermark(path, "step1"));
    }

    [Fact]
    public void FilePath_SanitizesKey()
    {
        // '/' は全プラットフォームで無効なファイル名文字（他の無効文字は OS 依存のため検証しない）
        var path = DeltaState.FilePath(_dir, "a/b");
        Assert.EndsWith(".state.json", path);
        var name = Path.GetFileName(path);
        Assert.DoesNotContain('/', name);
        Assert.Equal("a_b.state.json", name);
    }

    [Fact]
    public void SetWatermark_UpdatesExistingStep()
    {
        var path = DeltaState.FilePath(_dir, "job-b");
        DeltaState.SetWatermark(path, "step1", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        DeltaState.SetWatermark(path, "step1", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), DeltaState.GetWatermark(path, "step1"));
    }
}
