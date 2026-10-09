using SfUi.Etl.Engine;
using SfUi.Etl.Staging;
using Xunit;

namespace SfUi.Tests;

/// <summary>適用エンジン中核（バッチ ループ・リトライ・しきい値停止・checkpoint）を検証する。</summary>
public class EtlRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-etl-engine-" + Guid.NewGuid().ToString("N"));

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

    private static readonly string Pending = QueueStatus.Pending;
    private static readonly string Ok = QueueStatus.Ok;
    private static readonly string Failed = QueueStatus.Failed;
    private static readonly string Skipped = QueueStatus.Skipped;

    private (RunStagingStore Store, FakeTarget Target) Setup(int rows, int maxBatchSize = 200)
    {
        var store = RunStagingStore.Create(_dir, "run-" + Guid.NewGuid().ToString("N")[..6]);
        store.CreateStagingTable("Contact", new[] { new StagingColumn("LastName", StagingColumnType.Text) });
        store.InsertStagingRows("Contact", Enumerable.Range(1, rows).Select(i => new object?[] { "姓" + i }));
        store.EnqueueFromStaging("Contact");
        return (store, new FakeTarget { MaxBatchSize = maxBatchSize });
    }

    [Fact]
    public async Task HappyPath_AllSucceed_WithBatchSizes()
    {
        var (store, target) = Setup(5, maxBatchSize: 2);
        using var _ = store;

        var runner = new EtlRunner(store, target, "step1", "Contact");
        var result = await runner.RunAsync();

        Assert.Equal(5, result.Success);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Pending);
        Assert.Equal(5, result.Attempted);
        Assert.False(result.Stopped);
        Assert.Equal("completed", result.StopReason);
        Assert.Equal(new[] { 2, 2, 1 }, target.BatchSizes);

        var counts = store.CountQueueByStatus("Contact");
        Assert.Equal(5, counts[Ok]);
        Assert.NotNull(store.GetState("checkpoint:step1"));
    }

    [Fact]
    public async Task Crosswalk_Written_OnSuccess()
    {
        var (store, target) = Setup(3);
        using var _ = store;

        var runner = new EtlRunner(store, target, "step1", "Contact", new EtlApplyOptions(), crosswalkKeyIndex: 0);
        var result = await runner.RunAsync();

        Assert.Equal(3, result.Success);
        Assert.Equal(3, store.CountCrosswalk("step1", "Contact"));
        Assert.Equal("T1", store.LookupCrosswalk("step1", "Contact", "姓1"));
        Assert.Equal("T3", store.LookupCrosswalkByObject("Contact", "姓3"));
    }

    [Fact]
    public async Task PermanentFailure_MarksFailed_AndContinues()
    {
        var (store, target) = Setup(5);
        using var _ = store;
        target.Outcomes[3] = new Queue<RowApplyResult>(new[]
        {
            new RowApplyResult(3, false, Error: "REQUIRED_FIELD_MISSING"),
        });

        var result = await new EtlRunner(store, target, "step1", "Contact").RunAsync();

        Assert.Equal(4, result.Success);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Pending);

        var failed = store.FetchQueueRows("Contact", new[] { Failed });
        Assert.Single(failed);
        Assert.Equal("REQUIRED_FIELD_MISSING", failed[0].Error);
        Assert.Equal(1, failed[0].Attempts);
    }

    [Fact]
    public async Task TransientError_Retries_ThenSucceeds()
    {
        var (store, target) = Setup(5);
        using var _ = store;
        target.Outcomes[2] = new Queue<RowApplyResult>(new[]
        {
            new RowApplyResult(2, false, Error: "UNABLE_TO_LOCK_ROW", Transient: true),
            new RowApplyResult(2, true, TargetId: "T2"),
        });

        var result = await new EtlRunner(store, target, "step1", "Contact").RunAsync();

        Assert.Equal(5, result.Success);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Pending);
        Assert.Equal(6, result.Attempted);      // 5 行 + リトライ 1 回
        Assert.Equal(2, target.Calls);

        var ok = store.FetchQueueRows("Contact", new[] { Ok });
        Assert.Equal(5, ok.Count);
        Assert.Equal(2, ok.Single(r => r.RowId == 2).Attempts);   // 初回 + リトライ = 送信 2 回
    }

    [Fact]
    public async Task TransientError_ExceedingMaxRetries_BecomesFailed()
    {
        var (store, target) = Setup(1);
        using var _ = store;
        target.Outcomes[1] = new Queue<RowApplyResult>(new[]
        {
            new RowApplyResult(1, false, Error: "UNABLE_TO_LOCK_ROW", Transient: true),
            new RowApplyResult(1, false, Error: "UNABLE_TO_LOCK_ROW", Transient: true),
        });

        var result = await new EtlRunner(
            store, target, "step1", "Contact", new EtlApplyOptions { MaxRetries = 2 }).RunAsync();

        Assert.Equal(0, result.Success);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Pending);
        Assert.Equal(2, result.Attempted);      // 試行 2 回で打ち切り
        Assert.Equal(2, target.Calls);

        var failed = store.FetchQueueRows("Contact", new[] { Failed });
        Assert.Single(failed);
        Assert.Equal(2, failed[0].Attempts);
    }

    [Fact]
    public async Task ErrorRateThreshold_StopsEarly_LeavesPending()
    {
        var (store, target) = Setup(10, maxBatchSize: 2);
        using var _ = store;
        for (var rowId = 1L; rowId <= 6; rowId++)
        {
            target.Outcomes[rowId] = new Queue<RowApplyResult>(new[]
            {
                new RowApplyResult(rowId, false, Error: "INVALID_CROSS_REFERENCE_KEY"),
            });
        }

        var result = await new EtlRunner(store, target, "step1", "Contact", new EtlApplyOptions
        {
            MaxErrorRate = 0.3,
            MinRowsForErrorRate = 0,
            MaxConsecutiveFailures = 0,
        }).RunAsync();

        Assert.True(result.Stopped);
        Assert.Equal("error-rate", result.StopReason);
        Assert.Equal(2, result.Failed);         // 最初のバッチ（2 行）で停止
        Assert.Equal(0, result.Success);
        Assert.Equal(8, result.Pending);

        var counts = store.CountQueueByStatus("Contact");
        Assert.Equal(8, counts[Pending]);
        Assert.Equal(2, counts[Failed]);
    }

    [Fact]
    public async Task ConsecutiveFailures_Stop()
    {
        var (store, target) = Setup(6, maxBatchSize: 1);
        using var _ = store;
        for (var rowId = 1L; rowId <= 3; rowId++)
        {
            target.Outcomes[rowId] = new Queue<RowApplyResult>(new[]
            {
                new RowApplyResult(rowId, false, Error: "INVALID_TYPE"),
            });
        }

        var result = await new EtlRunner(store, target, "step1", "Contact", new EtlApplyOptions
        {
            MaxConsecutiveFailures = 3,
            MaxErrorRate = 1.0,
            MinRowsForErrorRate = 0,
        }).RunAsync();

        Assert.True(result.Stopped);
        Assert.Equal("consecutive-failures", result.StopReason);
        Assert.Equal(3, result.Failed);
        Assert.Equal(3, result.Pending);
    }

    [Fact]
    public async Task DryRun_DoesNotSend_KeepsPending()
    {
        var (store, target) = Setup(5);
        using var _ = store;

        var result = await new EtlRunner(store, target, "step1", "Contact").RunAsync(dryRun: true);

        Assert.Equal("dry-run", result.StopReason);
        Assert.Equal(5, result.Pending);
        Assert.Equal(0, target.Calls);
        Assert.Equal(5, store.CountQueueByStatus("Contact")[Pending]);
    }

    [Fact]
    public async Task PreflightFailure_BlocksRun()
    {
        var (store, target) = Setup(5);
        using var _ = store;
        target.TestResult = false;

        var result = await new EtlRunner(store, target, "step1", "Contact").RunAsync();

        Assert.True(result.Stopped);
        Assert.Equal("preflight-failed", result.StopReason);
        Assert.Equal(0, target.Calls);
        Assert.Equal(5, result.Pending);
    }

    [Fact]
    public async Task SkippedOutcome_MarksSkipped()
    {
        var (store, target) = Setup(3);
        using var _ = store;
        target.Outcomes[2] = new Queue<RowApplyResult>(new[]
        {
            new RowApplyResult(2, false, Skipped: true, Error: "空キーのためスキップ"),
        });

        var result = await new EtlRunner(store, target, "step1", "Contact").RunAsync();

        Assert.Equal(2, result.Success);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        Assert.Equal(1, store.CountQueueByStatus("Contact")[Skipped]);
    }

    [Fact]
    public async Task MissingOutcome_IsTreatedAsFailure()
    {
        var (store, target) = Setup(3);
        using var _ = store;
        target.DropRows.Add(2);

        var result = await new EtlRunner(store, target, "step1", "Contact").RunAsync();

        Assert.Equal(2, result.Success);
        Assert.Equal(1, result.Failed);
        var failed = store.FetchQueueRows("Contact", new[] { Failed });
        Assert.Equal("ターゲットから結果が返されませんでした。", failed[0].Error);
    }

    [Fact]
    public async Task Resume_AfterStop_ProcessesRemainingPending()
    {
        var (store, target) = Setup(6, maxBatchSize: 2);
        using var _ = store;
        for (var rowId = 1L; rowId <= 2; rowId++)
        {
            target.Outcomes[rowId] = new Queue<RowApplyResult>(new[]
            {
                new RowApplyResult(rowId, false, Error: "INVALID_TYPE"),
            });
        }

        var runner = new EtlRunner(store, target, "step1", "Contact", new EtlApplyOptions
        {
            MaxErrorRate = 0.3,
            MinRowsForErrorRate = 0,
            MaxConsecutiveFailures = 0,
        });
        var first = await runner.RunAsync();
        Assert.True(first.Stopped);

        // 再実行（全エラーの原因が解消された想定）
        target.Outcomes.Clear();
        var second = await runner.RunAsync();

        Assert.False(second.Stopped);
        Assert.Equal(4, second.Success);
        Assert.Equal(0, second.Pending);
        Assert.Equal(4, store.CountQueueByStatus("Contact")[Ok]);
    }

    /// <summary>テスト用ターゲット。行ごとの結果をスクリプトでき、呼び出しを記録する。</summary>
    private sealed class FakeTarget : IEtlTarget
    {
        public string Name => "fake";

        public int MaxBatchSize { get; set; } = 200;

        public bool TestResult { get; set; } = true;

        /// <summary>行 Id ごとの結果キュー（空になったら成功扱い）。</summary>
        public Dictionary<long, Queue<RowApplyResult>> Outcomes { get; } = new();

        /// <summary>結果を返さない行（異常系テスト用）。</summary>
        public HashSet<long> DropRows { get; } = new();

        public List<int> BatchSizes { get; } = new();

        public int Calls { get; private set; }

        public Task<bool> TestAsync(CancellationToken ct) => Task.FromResult(TestResult);

        public Task<EtlBatchResult> ApplyBatchAsync(EtlApplyContext context, IReadOnlyList<QueueRow> rows, CancellationToken ct)
        {
            Calls++;
            BatchSizes.Add(rows.Count);
            var results = new List<RowApplyResult>();
            foreach (var row in rows)
            {
                if (DropRows.Contains(row.RowId))
                {
                    continue;
                }

                var outcome = Outcomes.TryGetValue(row.RowId, out var queue) && queue.Count > 0
                    ? queue.Dequeue()
                    : new RowApplyResult(row.RowId, true, TargetId: "T" + row.RowId);
                results.Add(outcome);
            }

            return Task.FromResult(new EtlBatchResult(results));
        }
    }
}
