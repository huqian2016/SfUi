using System.Text;
using SfUi.Etl.Engine;
using SfUi.Etl.Expressions;
using SfUi.Etl.Sources;
using SfUi.Etl.Staging;
using SfUi.Etl.Targets;
using SfUi.Etl.Transforms;
using Xunit;

namespace SfUi.Tests;

/// <summary>ステップ実行（Prepare → Apply → 自動ロールバック）を通しで検証する。</summary>
public class EtlStepRunTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-etl-step-" + Guid.NewGuid().ToString("N"));

    public EtlStepRunTests() => Directory.CreateDirectory(_dir);

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

    private static RowMapper MapperFor(string[] columns, params FieldMapping[] mappings)
        => new(columns, mappings, new ExpressionEngine());

    [Fact]
    public async Task CsvToCsv_EndToEnd()
    {
        var csvPath = Path.Combine(_dir, "in.csv");
        File.WriteAllText(csvPath, "Id,名\r\n1,太郎\r\n2,花子\r\n", new UTF8Encoding(false));

        var outPath = Path.Combine(_dir, "out.csv");
        using var store = RunStagingStore.Create(_dir, "step-1");
        var plan = new EtlStepPlan
        {
            StepId = "step1",
            ObjectName = "Contact",
            Source = new CsvFileSource(csvPath),
            Mapper = MapperFor(new[] { "Id", "名" },
                new FieldMapping("Code", "[Id]"),
                new FieldMapping("Name", "[名]")),
            Target = new CsvFileTarget(outPath, new[] { "Code", "Name" }),
        };

        var result = await new EtlStepRun(store, plan).RunAsync();

        Assert.Equal(2, result.Loaded);
        Assert.Equal(2, result.Enqueued);
        Assert.NotNull(result.Apply);
        Assert.Equal(2, result.Apply!.Success);
        Assert.False(result.Apply.Stopped);
        Assert.Null(result.Rollback);

        var text = File.ReadAllText(outPath, new UTF8Encoding(false));
        Assert.Equal("Code,Name\r\n1,太郎\r\n2,花子\r\n", text);
    }

    [Fact]
    public async Task DryRun_DoesNotWriteOutput()
    {
        var csvPath = Path.Combine(_dir, "in2.csv");
        File.WriteAllText(csvPath, "Id\r\n1\r\n2\r\n", new UTF8Encoding(false));

        var outPath = Path.Combine(_dir, "out2.csv");
        using var store = RunStagingStore.Create(_dir, "step-2");
        var plan = new EtlStepPlan
        {
            StepId = "step1",
            ObjectName = "Contact",
            Source = new CsvFileSource(csvPath),
            Mapper = MapperFor(new[] { "Id" }, new FieldMapping("Code", "[Id]")),
            Target = new CsvFileTarget(outPath, new[] { "Code" }),
        };

        var result = await new EtlStepRun(store, plan).RunAsync(dryRun: true);

        Assert.Equal(2, result.Loaded);
        Assert.NotNull(result.Apply);
        Assert.Equal("dry-run", result.Apply!.StopReason);
        Assert.Equal(2, result.Apply.Pending);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public async Task Failure_TriggersAutoRollback()
    {
        using var store = RunStagingStore.Create(_dir, "step-3");
        var fake = new FakeJournalTarget { FailFromRowId = 6 };
        var plan = new EtlStepPlan
        {
            StepId = "step1",
            ObjectName = "Contact",
            Source = new ListSource(new[] { "K" }, Rows("k1", "k2", "k3", "k4", "k5", "k6", "k7", "k8", "k9", "k10")),
            Mapper = MapperFor(new[] { "K" }, new FieldMapping("Code", "[K]")),
            Target = fake,
            Revertable = fake,
        };

        var applyOptions = new EtlApplyOptions
        {
            BatchSize = 10,
            MaxErrorRate = 0.3,
            MinRowsForErrorRate = 0,
            MaxConsecutiveFailures = 0,
        };

        var result = await new EtlStepRun(store, plan, applyOptions).RunAsync();

        Assert.True(result.Apply!.Stopped);
        Assert.Equal("error-rate", result.Apply.StopReason);
        Assert.Equal(5, result.Apply.Success);
        Assert.Equal(5, result.Apply.Failed);

        Assert.NotNull(result.Rollback);
        Assert.Equal(5, result.Rollback!.Reverted);
        Assert.Equal(5, fake.RevertedJournalIds.Count);

        var journal = store.QueryJournal("Contact");
        Assert.Equal(5, journal.Count);
        Assert.All(journal, j => Assert.Equal("reverted", j.RevertStatus));
    }

    [Fact]
    public async Task Failure_WithoutAutoRollback_KeepsJournal()
    {
        using var store = RunStagingStore.Create(_dir, "step-4");
        var fake = new FakeJournalTarget { FailFromRowId = 5 };
        var plan = new EtlStepPlan
        {
            StepId = "step1",
            ObjectName = "Contact",
            Source = new ListSource(new[] { "K" }, Rows("k1", "k2", "k3", "k4", "k5", "k6")),
            Mapper = MapperFor(new[] { "K" }, new FieldMapping("Code", "[K]")),
            Target = fake,
            Revertable = fake,
        };

        var applyOptions = new EtlApplyOptions
        {
            BatchSize = 6,
            MaxErrorRate = 0.3,
            MinRowsForErrorRate = 0,
            MaxConsecutiveFailures = 0,
        };

        var result = await new EtlStepRun(store, plan, applyOptions).RunAsync(autoRollbackOnFailure: false);

        Assert.True(result.Apply!.Stopped);
        Assert.Equal(4, result.Apply.Success);
        Assert.Null(result.Rollback);
        Assert.Empty(fake.RevertedJournalIds);

        // 成功後の巻き戻し（手動リストア）は後から実行できる
        var rollback = await new EtlStepRun(store, plan, applyOptions).RollbackAsync();
        Assert.Equal(4, rollback.Reverted);
    }

    [Fact]
    public async Task Rollback_NotSupported_Throws()
    {
        using var store = RunStagingStore.Create(_dir, "step-5");
        var plan = new EtlStepPlan
        {
            StepId = "step1",
            ObjectName = "Contact",
            Source = new ListSource(new[] { "K" }, Rows("k1")),
            Mapper = MapperFor(new[] { "K" }, new FieldMapping("Code", "[K]")),
            Target = new CsvFileTarget(Path.Combine(_dir, "out5.csv"), new[] { "Code" }),
        };

        var stepRun = new EtlStepRun(store, plan);
        await Assert.ThrowsAsync<InvalidOperationException>(() => stepRun.RollbackAsync());
    }

    [Fact]
    public async Task PreRunBackup_RunsBeforeApply_OnlyWhenNotDryRun()
    {
        var csvPath = Path.Combine(_dir, "in6.csv");
        File.WriteAllText(csvPath, "Id\r\n1\r\n", new UTF8Encoding(false));

        using var store = RunStagingStore.Create(_dir, "step-6");
        var plan = new EtlStepPlan
        {
            StepId = "step1",
            ObjectName = "Contact",
            Source = new CsvFileSource(csvPath),
            Mapper = MapperFor(new[] { "Id" }, new FieldMapping("Code", "[Id]")),
            Target = new CsvFileTarget(Path.Combine(_dir, "out6.csv"), new[] { "Code" }),
        };

        var stepRun = new EtlStepRun(store, plan);
        var backupCalls = 0;
        var pendingAtBackup = -1;
        stepRun.PreRunBackup = _ =>
        {
            backupCalls++;
            pendingAtBackup = store.CountQueueByStatus("Contact").TryGetValue(QueueStatus.Pending, out var p) ? p : 0;
            return Task.FromResult<string?>("backup-001");
        };

        var dryRun = await stepRun.RunAsync(dryRun: true);
        Assert.Equal(0, backupCalls);
        Assert.Null(dryRun.BackupInfo);

        var result = await stepRun.RunAsync();
        Assert.Equal(1, backupCalls);
        Assert.Equal("backup-001", result.BackupInfo);

        // dry-run の残 pending = 1。フックが Prepare の後に動くなら 2 になるため、事前実行を証明できる
        Assert.Equal(1, pendingAtBackup);
    }

    private static object?[][] Rows(params string[] keys)
        => keys.Select(k => new object?[] { k }).ToArray();

    /// <summary>テスト用: 行 Id が <see cref="FailFromRowId"/> 以降なら恒久失敗、それ以外は成功 + journal 記録。</summary>
    private sealed class FakeJournalTarget : IEtlTarget, IEtlRevertable
    {
        public long FailFromRowId { get; set; } = long.MaxValue;

        public List<long> RevertedJournalIds { get; } = new();

        public string Name => "fake";

        public int MaxBatchSize => 200;

        public Task<bool> TestAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<EtlBatchResult> ApplyBatchAsync(EtlApplyContext context, IReadOnlyList<QueueRow> rows, CancellationToken ct)
        {
            var results = new List<RowApplyResult>();
            foreach (var row in rows)
            {
                if (row.RowId >= FailFromRowId)
                {
                    results.Add(new RowApplyResult(row.RowId, false, Error: "INVALID_TYPE"));
                    continue;
                }

                var journalId = context.Store.AddJournal(new JournalEntry(
                    context.StepId, context.ObjectName, RowOp.Insert, "T" + row.RowId, null, null, null));
                results.Add(new RowApplyResult(row.RowId, true, "T" + row.RowId, null, false, false, journalId));
            }

            return Task.FromResult(new EtlBatchResult(results));
        }

        public Task<EtlBatchResult> RevertBatchAsync(EtlApplyContext context, IReadOnlyList<JournalRow> entries, CancellationToken ct)
        {
            var results = new List<RowApplyResult>();
            foreach (var entry in entries)
            {
                RevertedJournalIds.Add(entry.Id);
                results.Add(new RowApplyResult(entry.Id, true));
            }

            return Task.FromResult(new EtlBatchResult(results));
        }
    }

    /// <summary>テスト用のインメモリ ソース。</summary>
    private sealed class ListSource : IEtlSource
    {
        private readonly List<object?[]> _rows;

        public ListSource(string[] columns, params object?[][] rows)
        {
            Columns = columns;
            _rows = rows.ToList();
        }

        public string Name => "list";

        public IReadOnlyList<string> Columns { get; }

        public IEnumerable<object?[]> ReadRows() => _rows;
    }
}
