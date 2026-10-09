using SfUi.Etl.Engine;
using SfUi.Etl.Staging;
using Xunit;

namespace SfUi.Tests;

/// <summary>journal ベースの巻き戻し（順序・バッチ・リトライ・失敗記録）を検証する。</summary>
public class JournalReverterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-etl-revert-" + Guid.NewGuid().ToString("N"));

    public JournalReverterTests() => Directory.CreateDirectory(_dir);

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

    private RunStagingStore CreateStore(string runId) => RunStagingStore.Create(_dir, runId);

    private static long AddInsertJournal(RunStagingStore store, string stepId = "step1", string objectName = "Account")
        => store.AddJournal(new JournalEntry(stepId, objectName, RowOp.Insert, "001A", null, null, null));

    [Fact]
    public async Task RevertsAll_InReverseOrder()
    {
        using var store = CreateStore("rev-1");
        var id1 = AddInsertJournal(store);
        var id2 = store.AddJournal(new JournalEntry("step1", "Account", RowOp.Update, "001B", "K1", "{\"Name\":\"old\"}", "{\"Name\":\"new\"}"));
        var id3 = store.AddJournal(new JournalEntry("step1", "Account", RowOp.Delete, "001C", "K2", "{\"Name\":\"del\"}", null));

        var fake = new FakeRevertable();
        var result = await new JournalReverter(store, fake, "step1", "Account").RevertAsync();

        Assert.Equal(3, result.Reverted);
        Assert.Equal(0, result.Failed);
        Assert.Single(fake.Batches);
        Assert.Equal(new[] { id3, id2, id1 }, fake.Batches[0]);   // 新しい順（Id 降順）

        var rows = store.QueryJournal("Account");
        Assert.All(rows, r => Assert.Equal("reverted", r.RevertStatus));
        Assert.All(rows, r => Assert.NotNull(r.RevertedAt));
    }

    [Fact]
    public async Task TransientRetry_ThenSuccess()
    {
        using var store = CreateStore("rev-2");
        var id1 = AddInsertJournal(store);

        var fake = new FakeRevertable();
        fake.Outcomes[id1] = new Queue<RowApplyResult>(new[]
        {
            new RowApplyResult(id1, false, Error: "UNABLE_TO_LOCK_ROW", Transient: true),
            new RowApplyResult(id1, true),
        });

        var result = await new JournalReverter(store, fake, "step1", "Account").RevertAsync();

        Assert.Equal(1, result.Reverted);
        Assert.Equal(0, result.Failed);
        Assert.Equal(2, fake.Batches.Count);
        Assert.Equal("reverted", store.QueryJournal("Account").Single().RevertStatus);
    }

    [Fact]
    public async Task PermanentFailure_MarkedFailed()
    {
        using var store = CreateStore("rev-3");
        var id1 = AddInsertJournal(store);

        var fake = new FakeRevertable();
        fake.Outcomes[id1] = new Queue<RowApplyResult>(new[]
        {
            new RowApplyResult(id1, false, Error: "INVALID_TYPE"),
        });

        var result = await new JournalReverter(store, fake, "step1", "Account").RevertAsync();

        Assert.Equal(0, result.Reverted);
        Assert.Equal(1, result.Failed);
        Assert.Single(fake.Batches);
        Assert.Equal("failed", store.QueryJournal("Account").Single().RevertStatus);
    }

    [Fact]
    public async Task TransientExhaustsRetries_MarkedFailed()
    {
        using var store = CreateStore("rev-4");
        var id1 = AddInsertJournal(store);

        var fake = new FakeRevertable();
        fake.Outcomes[id1] = new Queue<RowApplyResult>(new[]
        {
            new RowApplyResult(id1, false, Error: "SERVER_UNAVAILABLE", Transient: true),
            new RowApplyResult(id1, false, Error: "SERVER_UNAVAILABLE", Transient: true),
            new RowApplyResult(id1, false, Error: "SERVER_UNAVAILABLE", Transient: true),
        });

        var result = await new JournalReverter(
            store, fake, "step1", "Account", new RevertOptions { MaxRetries = 2 }).RevertAsync();

        Assert.Equal(0, result.Reverted);
        Assert.Equal(1, result.Failed);
        Assert.Equal(3, fake.Batches.Count);   // 初回 + リトライ 2 回
        Assert.Equal("failed", store.QueryJournal("Account").Single().RevertStatus);
    }

    [Fact]
    public async Task BatchedBySize()
    {
        using var store = CreateStore("rev-5");
        for (var i = 0; i < 5; i++)
        {
            AddInsertJournal(store);
        }

        var fake = new FakeRevertable();
        var result = await new JournalReverter(
            store, fake, "step1", "Account", new RevertOptions { BatchSize = 2 }).RevertAsync();

        Assert.Equal(5, result.Reverted);
        Assert.Equal(new[] { 2, 2, 1 }, fake.Batches.Select(b => b.Count));
    }

    [Fact]
    public async Task AlreadyReverted_Skipped()
    {
        using var store = CreateStore("rev-6");
        var id1 = AddInsertJournal(store);
        var id2 = AddInsertJournal(store);
        store.MarkReverted(id1, "reverted");

        var fake = new FakeRevertable();
        var result = await new JournalReverter(store, fake, "step1", "Account").RevertAsync();

        Assert.Equal(1, result.Reverted);
        Assert.Single(fake.Batches);
        Assert.Equal(new[] { id2 }, fake.Batches[0]);
    }

    [Fact]
    public async Task OtherStepEntries_Filtered()
    {
        using var store = CreateStore("rev-7");
        var id1 = AddInsertJournal(store, stepId: "step1");
        AddInsertJournal(store, stepId: "step2");

        var fake = new FakeRevertable();
        var result = await new JournalReverter(store, fake, "step1", "Account").RevertAsync();

        Assert.Equal(1, result.Reverted);
        Assert.Equal(new[] { id1 }, fake.Batches[0]);

        var others = store.QueryJournal("Account").Where(r => r.StepId == "step2").ToList();
        Assert.Null(others[0].RevertedAt);
    }

    /// <summary>テスト用の巻き戻しターゲット（行ごとの結果をスクリプトでき、呼び出しを記録する）。</summary>
    private sealed class FakeRevertable : IEtlRevertable
    {
        public Dictionary<long, Queue<RowApplyResult>> Outcomes { get; } = new();

        public List<List<long>> Batches { get; } = new();

        public Task<EtlBatchResult> RevertBatchAsync(EtlApplyContext context, IReadOnlyList<JournalRow> entries, CancellationToken ct)
        {
            Batches.Add(entries.Select(e => e.Id).ToList());
            var results = new List<RowApplyResult>();
            foreach (var entry in entries)
            {
                var outcome = Outcomes.TryGetValue(entry.Id, out var queue) && queue.Count > 0
                    ? queue.Dequeue()
                    : new RowApplyResult(entry.Id, true);
                results.Add(outcome);
            }

            return Task.FromResult(new EtlBatchResult(results));
        }
    }
}
