using SfUi.Etl.Engine;
using SfUi.Etl.Expressions;
using SfUi.Etl.Sources;
using SfUi.Etl.Staging;
using SfUi.Etl.Targets;
using SfUi.Etl.Transforms;
using Xunit;

namespace SfUi.Tests;

public class EtlRetryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-retry-" + Guid.NewGuid().ToString("N"));

    public EtlRetryTests() => Directory.CreateDirectory(_dir);

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
    public async Task Retry_ReusesStagedQueue_AndSucceedsAfterFix()
    {
        using var store = RunStagingStore.Create(Path.Combine(_dir, "run1"), "run1");
        var source = new CountingSource(
            new[] { "Id", "Name" },
            new object?[] { "1", "Alpha" },
            new object?[] { "2", "Beta" },
            new object?[] { "3", "Gamma" });
        var mapper = new RowMapper(
            source.Columns,
            new[] { new FieldMapping("Id", "[Id]"), new FieldMapping("Name", "[Name]") },
            new ExpressionEngine());
        var target = new FlakyTarget { FailingRowIds = { 2 } };
        var plan = new EtlStepPlan { StepId = "step1", ObjectName = "Items", Source = source, Mapper = mapper, Target = target };
        var step = new EtlStepRun(store, plan);

        step.Prepare();
        var first = await step.ApplyAsync();
        Assert.Equal(2, first.Success);
        Assert.Equal(1, first.Failed);
        Assert.False(first.Stopped);
        Assert.Equal(1, source.ReadCount);

        // 原因を取り除いてから失敗行のみ再実行（ソースは再読込しない）
        target.FailingRowIds.Clear();
        Assert.Equal(1, store.ResetFailedToPending("Items"));

        var runner = new EtlRunner(store, target, "step1", "Items");
        var retry = await runner.RunAsync();

        Assert.Equal(1, retry.Success);
        Assert.Equal(0, retry.Failed);
        Assert.Equal(0, retry.Pending);
        Assert.Equal(1, source.ReadCount);   // ★ステージング済みキューを再利用（ソース再読込なし）
        var counts = store.CountQueueByStatus("Items");
        Assert.Equal(3, counts[QueueStatus.Ok]);
        Assert.DoesNotContain(QueueStatus.Failed, counts.Keys);
    }

    [Fact]
    public async Task ResetFailedToPending_AffectsOnlyFailedRows_AndResetsAttempts()
    {
        using var store = RunStagingStore.Create(Path.Combine(_dir, "run2"), "run2");
        store.CreateStagingTable("T", new[] { new StagingColumn("Name", StagingColumnType.Text) });
        store.InsertStagingRows("T", new[] { new object?[] { "a" }, new object?[] { "b" }, new object?[] { "c" } });
        store.EnqueueFromStaging("T");

        store.MarkQueueRow("T", 1, QueueStatus.Failed, error: "boom", incrementAttempts: true);
        store.MarkQueueRow("T", 2, QueueStatus.Pending);
        store.MarkQueueRow("T", 3, QueueStatus.Ok, targetId: "id3", incrementAttempts: true);

        Assert.Equal(1, store.ResetFailedToPending("T"));

        var counts = store.CountQueueByStatus("T");
        Assert.Equal(2, counts[QueueStatus.Pending]);
        Assert.Equal(1, counts[QueueStatus.Ok]);
        Assert.DoesNotContain(QueueStatus.Failed, counts.Keys);

        // 試行回数とエラーがリセットされている（pending になった行を取得して確認）
        var pending = store.FetchQueueRows("T", new[] { QueueStatus.Pending }, 10, 0);
        var reset = pending.Single(r => r.RowId == 1);
        Assert.Equal(0, reset.Attempts);
        Assert.Null(reset.Error);
    }

    [Fact]
    public void QueueTableExists_DistinguishesMissingTable()
    {
        using var store = RunStagingStore.Create(Path.Combine(_dir, "run3"), "run3");
        Assert.False(store.QueueTableExists("Nope"));

        store.CreateStagingTable("Nope", new[] { new StagingColumn("Name", StagingColumnType.Text) });
        store.InsertStagingRows("Nope", new[] { new object?[] { "x" } });
        store.EnqueueFromStaging("Nope");
        Assert.True(store.QueueTableExists("Nope"));
    }

    private sealed class CountingSource : IEtlSource
    {
        private readonly object?[][] _rows;

        public CountingSource(string[] columns, params object?[][] rows)
        {
            Columns = columns;
            _rows = rows;
        }

        public int ReadCount { get; private set; }

        public string Name => "counting";

        public IReadOnlyList<string> Columns { get; }

        public IEnumerable<object?[]> ReadRows()
        {
            ReadCount++;
            return _rows;
        }
    }

    private sealed class FlakyTarget : IEtlTarget
    {
        public HashSet<long> FailingRowIds { get; } = new();

        public string Name => "flaky";

        public int MaxBatchSize => 200;

        public Task<bool> TestAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<EtlBatchResult> ApplyBatchAsync(
            EtlApplyContext context,
            IReadOnlyList<QueueRow> rows,
            CancellationToken cancellationToken)
        {
            var results = rows
                .Select(row => FailingRowIds.Contains(row.RowId)
                    ? new RowApplyResult(row.RowId, false, Error: "unit fail")
                    : new RowApplyResult(row.RowId, true, TargetId: "t" + row.RowId))
                .ToList();
            return Task.FromResult(new EtlBatchResult(results));
        }
    }
}
