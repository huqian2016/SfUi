using System.Globalization;
using SfUi.Etl.Engine;
using SfUi.Etl.Expressions;
using SfUi.Etl.Sources;
using SfUi.Etl.Staging;
using SfUi.Etl.Transforms;
using Xunit;

namespace SfUi.Tests;

/// <summary>マルチステップ ジョブ（親 → 子、LOOKUP による Id 受渡し、逆順ロールバック）を検証する。</summary>
public class EtlJobRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-etl-job-" + Guid.NewGuid().ToString("N"));

    public EtlJobRunnerTests() => Directory.CreateDirectory(_dir);

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
    public async Task ParentChild_TwoSteps_LookupResolvesParentIds()
    {
        using var store = RunStagingStore.Create(_dir, "job-1");

        var host = new ExpressionHost();
        EtlCrosswalk.WireLookup(host, store);

        var parents = new RecordingTarget("001");
        var children = new RecordingTarget("003");

        // step1: Key → Account.ExtId__c（crosswalk キー = ExtId__c）
        var parentMapper = new RowMapper(
            new[] { "Key" },
            new[] { new FieldMapping("ExtId__c", "[Key]") },
            new ExpressionEngine(host));

        // step2: PKey → Contact.AccountId は LOOKUP で親の target_id を解決
        var childMapper = new RowMapper(
            new[] { "PKey", "Name" },
            new[]
            {
                new FieldMapping("AccountId", "LOOKUP(\"Parent\", \"ExtId__c\", [PKey])"),
                new FieldMapping("LastName", "[Name]"),
            },
            new ExpressionEngine(host));

        var parentPlan = new EtlStepPlan
        {
            StepId = "step1",
            ObjectName = "Parent",
            Source = new ListSource(new[] { "Key" }, R("K1"), R("K2")),
            Mapper = parentMapper,
            Target = parents,
            CrosswalkKeyField = "ExtId__c",
        };
        var childPlan = new EtlStepPlan
        {
            StepId = "step2",
            ObjectName = "Child",
            Source = new ListSource(new[] { "PKey", "Name" }, new object?[] { "K1", "c1" }, new object?[] { "K2", "c2" }),
            Mapper = childMapper,
            Target = children,
        };

        var job = new EtlJobRunner(store, new EtlJobPlan { JobName = "test", Steps = new[] { parentPlan, childPlan } });
        var result = await job.RunAsync();

        Assert.False(result.Stopped);
        Assert.Equal(new[] { "step1", "step2" }, result.StepIds);
        Assert.Equal(2, result.Steps[0].Apply!.Success);
        Assert.Equal(2, result.Steps[1].Apply!.Success);

        // crosswalk に親の Id が記録されている
        Assert.Equal("001K1", store.LookupCrosswalk("step1", "Parent", "K1"));
        Assert.Equal(2, store.CountCrosswalk("step1", "Parent"));

        // 子の AccountId は親の target_id で解決されている
        Assert.Equal("001K1", children.AppliedValues[0][0]);
        Assert.Equal("001K2", children.AppliedValues[1][0]);
        Assert.Equal("c1", children.AppliedValues[0][1]);
    }

    [Fact]
    public async Task Stop_Propagates_AndRollsBackChildFirst()
    {
        using var store = RunStagingStore.Create(_dir, "job-2");
        var order = new List<string>();
        var parent = new RevertableRecordingTarget("parent", order);
        var child = new RevertableRecordingTarget("child", order) { FailFromRowId = 2 };

        var parentPlan = new EtlStepPlan
        {
            StepId = "step1",
            ObjectName = "Parent",
            Source = new ListSource(new[] { "Key" }, R("K1"), R("K2")),
            Mapper = SimpleMapper("Key", "ExtId__c"),
            Target = parent,
            Revertable = parent,
        };
        var childPlan = new EtlStepPlan
        {
            StepId = "step2",
            ObjectName = "Child",
            Source = new ListSource(new[] { "Key" }, R("K1"), R("K2")),
            Mapper = SimpleMapper("Key", "AccountId"),
            Target = child,
            Revertable = child,
        };

        var options = new EtlApplyOptions
        {
            MaxErrorRate = 0.3,
            MinRowsForErrorRate = 0,
            MaxConsecutiveFailures = 0,
        };

        var job = new EtlJobRunner(store, new EtlJobPlan { JobName = "test", Steps = new[] { parentPlan, childPlan } }, options);
        var result = await job.RunAsync();

        Assert.True(result.Stopped);
        Assert.Equal("error-rate", result.StopReason);
        Assert.Equal(new[] { "step1", "step2" }, result.StepIds);

        // 子 → 親の順に巻き戻し
        Assert.Equal(new[] { "child", "parent" }, order);
        Assert.Equal(2, result.Rollbacks.Count);

        var journal = store.QueryJournal();
        Assert.Equal(3, journal.Count);   // 親 2 + 子 1
        Assert.All(journal, j => Assert.Equal("reverted", j.RevertStatus));
    }

    [Fact]
    public void CrosswalkKeyField_NotInMappings_Throws()
    {
        using var store = RunStagingStore.Create(_dir, "job-3");
        var plan = new EtlStepPlan
        {
            StepId = "step1",
            ObjectName = "X",
            Source = new ListSource(new[] { "A" }, R("v1")),
            Mapper = SimpleMapper("A", "B"),
            Target = new RecordingTarget("x"),
            CrosswalkKeyField = "Missing",
        };

        Assert.Throws<ArgumentException>(() => new EtlStepRun(store, plan));
    }

    private static object?[] R(params object?[] values) => values;

    private static RowMapper SimpleMapper(string column, string field)
        => new(
            new[] { column },
            new[] { new FieldMapping(field, "[" + column + "]") },
            new ExpressionEngine());

    /// <summary>適用結果を記録するターゲット（TargetId = prefix + 先頭値）。</summary>
    private sealed class RecordingTarget : IEtlTarget
    {
        private readonly string _idPrefix;

        public RecordingTarget(string idPrefix)
        {
            _idPrefix = idPrefix;
        }

        public List<object?[]> AppliedValues { get; } = new();

        public string Name => "recording";

        public int MaxBatchSize => 200;

        public Task<bool> TestAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<EtlBatchResult> ApplyBatchAsync(EtlApplyContext context, IReadOnlyList<QueueRow> rows, CancellationToken ct)
        {
            var results = new List<RowApplyResult>();
            foreach (var row in rows)
            {
                AppliedValues.Add(row.Values.ToArray());
                var id = _idPrefix + Convert.ToString(
                    row.Values.Length > 0 ? row.Values[0] : null,
                    CultureInfo.InvariantCulture);
                results.Add(new RowApplyResult(row.RowId, true, id));
            }

            return Task.FromResult(new EtlBatchResult(results));
        }
    }

    /// <summary>journal を書き、巻き戻し順を記録するターゲット。</summary>
    private sealed class RevertableRecordingTarget : IEtlTarget, IEtlRevertable
    {
        private readonly string _name;
        private readonly List<string> _revertOrder;

        public RevertableRecordingTarget(string name, List<string> revertOrder)
        {
            _name = name;
            _revertOrder = revertOrder;
        }

        public long FailFromRowId { get; set; } = long.MaxValue;

        public string Name => _name;

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

                var targetId = "T" + _name + row.RowId;
                var journalId = context.Store.AddJournal(new JournalEntry(
                    context.StepId, context.ObjectName, RowOp.Insert, targetId, null, null, null));
                results.Add(new RowApplyResult(row.RowId, true, targetId, null, false, false, journalId));
            }

            return Task.FromResult(new EtlBatchResult(results));
        }

        public Task<EtlBatchResult> RevertBatchAsync(EtlApplyContext context, IReadOnlyList<JournalRow> entries, CancellationToken ct)
        {
            _revertOrder.Add(_name);
            var results = entries.Select(e => new RowApplyResult(e.Id, true)).ToList();
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
