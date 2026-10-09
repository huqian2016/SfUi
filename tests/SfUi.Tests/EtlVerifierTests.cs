using SfUi.Etl.Staging;
using SfUi.Etl.Verification;
using Xunit;

namespace SfUi.Tests;

public class EtlVerifierTests : IDisposable
{
    private readonly List<string> _dirs = new();

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task Verify_Passes_WhenTargetMatchesStaging()
    {
        using var store = NewStore();
        var columns = new[] { new StagingColumn("Name", StagingColumnType.Text), new StagingColumn("Amount", StagingColumnType.Integer) };
        Seed(store, "Items", columns, ("A1", 100L), ("A2", 200L));

        var fetcher = new StubFetcher();
        fetcher.Records["t1"] = new() { ["Name"] = "A1", ["Amount"] = 100 };
        fetcher.Records["t2"] = new() { ["Name"] = "A2", ["Amount"] = 200 };

        var verifier = new EtlVerifier(store, fetcher, "step1", "Items", columns);
        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal(2, result.OkRows);
        Assert.Equal(2, result.FetchedRows);
        Assert.Empty(result.MissingKeys);
        Assert.Equal(2, result.SampledRows);
        Assert.Equal(4, result.ComparedValues);
        Assert.Equal(4, result.MatchedValues);
        var total = Assert.Single(result.Totals);
        Assert.Equal("Amount", total.Field);
        Assert.Equal(300m, total.Expected);
        Assert.Equal(300m, total.Actual);
    }

    [Fact]
    public async Task Verify_DetectsMissingRecord()
    {
        using var store = NewStore();
        var columns = new[] { new StagingColumn("Name", StagingColumnType.Text) };
        Seed(store, "Items", columns, "A1", "A2");

        var fetcher = new StubFetcher();
        fetcher.Records["t1"] = new() { ["Name"] = "A1" };

        var verifier = new EtlVerifier(store, fetcher, "step1", "Items", columns);
        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal(new[] { "t2" }, result.MissingKeys);
        Assert.Equal(1, result.FetchedRows);
    }

    [Fact]
    public async Task Verify_DetectsValueMismatch()
    {
        using var store = NewStore();
        var columns = new[] { new StagingColumn("Name", StagingColumnType.Text), new StagingColumn("Amount", StagingColumnType.Integer) };
        Seed(store, "Items", columns, ("A1", 100L), ("A2", 200L));

        var fetcher = new StubFetcher();
        fetcher.Records["t1"] = new() { ["Name"] = "A1", ["Amount"] = 100 };
        fetcher.Records["t2"] = new() { ["Name"] = "A2", ["Amount"] = 999 };

        var verifier = new EtlVerifier(store, fetcher, "step1", "Items", columns);
        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.False(result.Passed);
        var mismatch = Assert.Single(result.Mismatches);
        Assert.Equal("Amount", mismatch.Field);
        Assert.Equal(2, mismatch.RowId);
        Assert.Equal("200", mismatch.Expected);
        Assert.Equal("999", mismatch.Actual);
        Assert.Equal(3, result.MatchedValues);
    }

    [Fact]
    public async Task Verify_DetectsTotalMismatch_EvenWhenSamplePasses()
    {
        using var store = NewStore();
        var columns = new[] { new StagingColumn("Name", StagingColumnType.Text), new StagingColumn("Amount", StagingColumnType.Integer) };
        Seed(store, "Items", columns, ("A1", 100L), ("A2", 200L));

        var fetcher = new StubFetcher();
        fetcher.Records["t1"] = new() { ["Name"] = "A1", ["Amount"] = 100 };
        fetcher.Records["t2"] = new() { ["Name"] = "A2", ["Amount"] = 999 };

        // サンプル 1 件（先頭のみ）→ 値の不一致はサンプル外、合計照合のみが検知する
        var verifier = new EtlVerifier(
            store,
            fetcher,
            "step1",
            "Items",
            columns,
            new EtlVerificationOptions { SampleSize = 1 });
        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Empty(result.Mismatches);
        Assert.Equal(1, result.SampledRows);
        var total = Assert.Single(result.Totals);
        Assert.Equal(300m, total.Expected);
        Assert.Equal(1099m, total.Actual);
    }

    [Fact]
    public async Task Verify_DeleteRows_ChecksAbsence()
    {
        using var store = NewStore();
        var columns = new[] { new StagingColumn("Name", StagingColumnType.Text) };
        store.CreateStagingTable("Old", columns);
        store.InsertStagingRows("Old", new[] { new object?[] { "Gone" } });
        store.EnqueueFromStaging("Old", RowOp.Delete);
        store.MarkQueueRow("Old", 1, QueueStatus.Ok, targetId: "tD", incrementAttempts: true);

        var fetcher = new StubFetcher();
        fetcher.Records["tD"] = new() { ["Name"] = "Gone" };

        var verifier = new EtlVerifier(store, fetcher, "step1", "Old", columns);
        var result = await verifier.VerifyAsync(CancellationToken.None);
        Assert.False(result.Passed);
        Assert.Equal(1, result.DeletedRows);
        Assert.Equal(new[] { "tD" }, result.UndeletedKeys);

        // ターゲットから消えていれば合格
        fetcher.Records.Clear();
        var result2 = await verifier.VerifyAsync(CancellationToken.None);
        Assert.True(result2.Passed);
        Assert.Equal(1, result2.DeletedRows);
        Assert.Equal(0, result2.VerifiedRows);
    }

    [Fact]
    public void SelectSample_SpreadsEvenly()
    {
        var sample = EtlVerifier.SelectSample(10, 3);
        Assert.Equal(3, sample.Count);
        Assert.Equal(0, sample[0]);
        Assert.Equal(9, sample[^1]);
        Assert.True(sample[0] < sample[1] && sample[1] < sample[2]);

        Assert.Equal(new[] { 0 }, EtlVerifier.SelectSample(2, 1));
        Assert.Equal(new[] { 0, 1, 2 }, EtlVerifier.SelectSample(3, 5));
        Assert.Empty(EtlVerifier.SelectSample(0, 5));
        Assert.Empty(EtlVerifier.SelectSample(5, 0));
    }

    [Theory]
    [InlineData(100L, "100", true)]
    [InlineData("100.0", 100.0, true)]
    [InlineData(true, "True", true)]
    [InlineData(1L, "true", true)]
    [InlineData(0L, "false", true)]
    [InlineData("2026-01-03", null, false)]
    [InlineData(" a ", "a", true)]
    [InlineData(null, "", true)]
    [InlineData("a", "b", false)]
    [InlineData("true", "false", false)]
    [InlineData(2L, "true", false)]
    public void Equal_NormalizesAcrossTypes(object? expected, object? actual, bool expectedResult)
        => Assert.Equal(expectedResult, EtlVerificationValues.Equal(expected, actual));

    [Fact]
    public void Equal_ComparesDatesAndTimes()
    {
        Assert.True(EtlVerificationValues.Equal("2026-01-03", new DateTime(2026, 1, 3)));
        Assert.True(EtlVerificationValues.Equal("2026-01-03T00:00:00", "2026-01-03"));
        Assert.False(EtlVerificationValues.Equal("2026-01-03T12:00:00", "2026-01-03"));
    }

    private RunStagingStore NewStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sfui-verify-" + Guid.NewGuid().ToString("N"));
        _dirs.Add(dir);
        return RunStagingStore.Create(dir, "run-test");
    }

    private static void Seed(RunStagingStore store, string objectName, IReadOnlyList<StagingColumn> columns, params object?[] names)
    {
        store.CreateStagingTable(objectName, columns);
        var rows = names.Select((n, i) => new object?[] { n, (long)(i + 1) * 100 }).ToList();
        if (columns.Count == 1)
        {
            rows = names.Select(n => new object?[] { n }).ToList();
        }

        store.InsertStagingRows(objectName, rows);
        store.EnqueueFromStaging(objectName);
        for (var i = 0; i < rows.Count; i++)
        {
            store.MarkQueueRow(objectName, i + 1, QueueStatus.Ok, targetId: "t" + (i + 1), incrementAttempts: true);
        }
    }

    private static void Seed(RunStagingStore store, string objectName, IReadOnlyList<StagingColumn> columns, string name, string name2)
        => Seed(store, objectName, columns, (object)name, (object)name2);

    private static void Seed(RunStagingStore store, string objectName, IReadOnlyList<StagingColumn> columns, string name)
        => Seed(store, objectName, columns, (object)name);

    private static void Seed(RunStagingStore store, string objectName, IReadOnlyList<StagingColumn> columns, params (string Name, long Amount)[] rows)
    {
        store.CreateStagingTable(objectName, columns);
        store.InsertStagingRows(objectName, rows.Select(r => new object?[] { r.Name, r.Amount }));
        store.EnqueueFromStaging(objectName);
        for (var i = 0; i < rows.Length; i++)
        {
            store.MarkQueueRow(objectName, i + 1, QueueStatus.Ok, targetId: "t" + (i + 1), incrementAttempts: true);
        }
    }

    private sealed class StubFetcher : IEtlRecordFetcher
    {
        public Dictionary<string, Dictionary<string, object?>> Records { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<EtlFetchedRecord>> FetchByKeysAsync(
            IReadOnlyCollection<string> keys,
            IReadOnlyList<string> fields,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<EtlFetchedRecord> list = keys
                .Where(Records.ContainsKey)
                .Select(k => new EtlFetchedRecord(k, Records[k]))
                .ToList();
            return Task.FromResult(list);
        }
    }
}
