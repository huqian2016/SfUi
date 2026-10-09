using SfUi.Etl.Staging;
using Xunit;

namespace SfUi.Tests;

/// <summary>2 段ステージング ストア（src/dst SQLite・crosswalk・journal・適用キュー）を検証する。</summary>
public class RunStagingStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-etl-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // WAL ファイルの解放待ち等は無視
        }
    }

    [Fact]
    public void Create_CreatesBothDatabases_And_Meta()
    {
        using var store = RunStagingStore.Create(_dir, "run-001");

        Assert.True(File.Exists(store.SourceDbPath));
        Assert.True(File.Exists(store.TargetDbPath));
        Assert.Equal("run-001", store.RunId);

        store.SetState("checkpoint", "step1:batch3");
        Assert.Equal("step1:batch3", store.GetState("checkpoint"));
        Assert.Null(store.GetState("missing"));
    }

    [Fact]
    public void InputTable_RoundTrips_TextRows()
    {
        using var store = RunStagingStore.Create(_dir, "run-002");
        store.CreateInputTable("csv1", new[] { "Id", "Name", "Memo" });

        var count = store.InsertInputRows("csv1", new[]
        {
            new string?[] { "1", "株式会社テスト", "メモ" },
            new string?[] { "2", "ACME", null },
            new string?[] { "3", "テスト商事", "a,b" },
        });

        Assert.Equal(3, count);
        Assert.Equal(3, store.CountInputRows("csv1"));
    }

    [Fact]
    public void StagingTable_RoundTrips_TypedValues()
    {
        using var store = RunStagingStore.Create(_dir, "run-003");
        store.CreateStagingTable("Account", new[]
        {
            new StagingColumn("Name", StagingColumnType.Text),
            new StagingColumn("NumberOfEmployees", StagingColumnType.Integer),
            new StagingColumn("AnnualRevenue", StagingColumnType.Real),
            new StagingColumn("Active__c", StagingColumnType.Boolean),
            new StagingColumn("CreatedDate", StagingColumnType.DateTime),
        });

        store.InsertStagingRows("Account", new[]
        {
            new object?[] { "テスト株式会社", 100L, 1234.5, true, new DateTime(2026, 10, 9, 12, 34, 56) },
            new object?[] { "ACME", null, null, false, null },
        });

        Assert.Equal(2, store.CountStagingRows("Account"));
        Assert.Equal(
            new[] { "Name", "NumberOfEmployees", "AnnualRevenue", "Active__c", "CreatedDate" },
            store.GetStagingColumns("Account"));

        var rows = store.ReadStagingRows("Account").ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("テスト株式会社", rows[0][0]);
        Assert.Equal(100L, rows[0][1]);
        Assert.Equal(1234.5, rows[0][2]);
        Assert.Equal(1L, rows[0][3]);
        Assert.Equal("2026-10-09T12:34:56", rows[0][4]);
        Assert.Null(rows[1][1]);
    }

    [Fact]
    public void EnqueueFromStaging_Copies_With_Pending_Status()
    {
        using var store = RunStagingStore.Create(_dir, "run-004");
        store.CreateStagingTable("Contact", new[]
        {
            new StagingColumn("LastName", StagingColumnType.Text),
            new StagingColumn("AccountId", StagingColumnType.Text),
        });
        store.InsertStagingRows("Contact", new[]
        {
            new object?[] { "山田", "001xx" },
            new object?[] { "鈴木", "001yy" },
            new object?[] { "佐藤", null },
        });

        var copied = store.EnqueueFromStaging("Contact");
        Assert.Equal(3, copied);

        var pending = store.FetchQueueRows("Contact", new[] { QueueStatus.Pending });
        Assert.Equal(3, pending.Count);
        Assert.Equal(RowOp.Insert, pending[0].Op);
        Assert.Equal("山田", pending[0].Values[0]);
        Assert.Equal(0, pending[0].Attempts);

        Assert.Equal(3, store.CountQueueByStatus("Contact")[QueueStatus.Pending]);
    }

    [Fact]
    public void MarkQueueRow_Updates_Status_And_Journal_Reference()
    {
        using var store = RunStagingStore.Create(_dir, "run-005");
        store.CreateStagingTable("Contact", new[] { new StagingColumn("LastName", StagingColumnType.Text) });
        store.InsertStagingRows("Contact", new[]
        {
            new object?[] { "山田" },
            new object?[] { "鈴木" },
        });
        store.EnqueueFromStaging("Contact");
        var rows = store.FetchQueueRows("Contact");
        Assert.Equal(2, rows.Count);

        store.MarkQueueRow("Contact", rows[0].RowId, QueueStatus.Ok, targetId: "003AAA", journalId: 7, incrementAttempts: true);
        store.MarkQueueRow("Contact", rows[1].RowId, QueueStatus.Failed, error: "REQUIRED_FIELD_MISSING", incrementAttempts: true);

        var ok = store.FetchQueueRows("Contact", new[] { QueueStatus.Ok });
        Assert.Single(ok);
        Assert.Equal("003AAA", ok[0].TargetId);
        Assert.Equal(7, ok[0].JournalId);
        Assert.Equal(1, ok[0].Attempts);

        var failed = store.FetchQueueRows("Contact", new[] { QueueStatus.Failed });
        Assert.Single(failed);
        Assert.Equal("REQUIRED_FIELD_MISSING", failed[0].Error);

        var counts = store.CountQueueByStatus("Contact");
        Assert.Equal(1, counts[QueueStatus.Ok]);
        Assert.Equal(1, counts[QueueStatus.Failed]);
    }

    [Fact]
    public void Crosswalk_Upsert_And_Lookup()
    {
        using var store = RunStagingStore.Create(_dir, "run-006");

        store.UpsertCrosswalk("step2", "Contact", "SRC-1", "003AAA");
        Assert.Equal("003AAA", store.LookupCrosswalk("step2", "Contact", "SRC-1"));
        Assert.Null(store.LookupCrosswalk("step2", "Contact", "SRC-9"));

        store.UpsertCrosswalk("step2", "Contact", "SRC-1", "003BBB");
        Assert.Equal("003BBB", store.LookupCrosswalk("step2", "Contact", "SRC-1"));
        Assert.Equal(1, store.CountCrosswalk("step2", "Contact"));
    }

    [Fact]
    public void Journal_Append_Query_And_Revert()
    {
        using var store = RunStagingStore.Create(_dir, "run-007");

        var id1 = store.AddJournal(new JournalEntry(
            "step1", "Account", RowOp.Update, "001AAA", "SRC-1", "{\"Name\":\"old\"}", "{\"Name\":\"new\"}"));
        var id2 = store.AddJournal(new JournalEntry(
            "step1", "Account", RowOp.Delete, "001BBB", "SRC-2", "{\"Name\":\"del\"}", null));
        Assert.True(id2 > id1);

        var rows = store.QueryJournal("Account");
        Assert.Equal(2, rows.Count);
        Assert.Equal(RowOp.Update, rows[0].Op);
        Assert.Equal("{\"Name\":\"old\"}", rows[0].BeforeJson);
        Assert.Null(rows[1].AfterJson);
        Assert.Equal("run-007", rows[0].RunId);

        store.MarkReverted(id2, "reverted");
        var again = store.QueryJournal("Account");
        Assert.NotNull(again[1].RevertedAt);
        Assert.Equal("reverted", again[1].RevertStatus);
        Assert.Null(again[0].RevertedAt);
    }

    [Fact]
    public void Reopen_Preserves_Data()
    {
        using (var store = RunStagingStore.Create(_dir, "run-008"))
        {
            store.CreateStagingTable("Account", new[] { new StagingColumn("Name", StagingColumnType.Text) });
            store.InsertStagingRows("Account", new[] { new object?[] { "A" }, new object?[] { "B" } });
            store.EnqueueFromStaging("Account");
            store.SetState("k", "v");
            store.AddJournal(new JournalEntry("step1", "Account", RowOp.Insert, "001X", "A", null, null));
        }

        using var reopened = RunStagingStore.Open(_dir);

        Assert.Equal("run-008", reopened.RunId);
        Assert.Equal(2, reopened.CountStagingRows("Account"));
        Assert.Equal(2, reopened.CountQueueByStatus("Account")[QueueStatus.Pending]);
        Assert.Equal("v", reopened.GetState("k"));
        Assert.Single(reopened.QueryJournal("Account"));
        Assert.Equal(2, reopened.FetchQueueRows("Account").Count);
    }

    [Fact]
    public void SafeIdentifier_Normalizes()
    {
        Assert.Equal("取引先", RunStagingStore.SafeIdentifier("取引先"));
        Assert.Equal("My_Object_v2", RunStagingStore.SafeIdentifier("My Object-v2"));
        Assert.Equal("_2nd", RunStagingStore.SafeIdentifier("2nd"));
        Assert.Equal("stg_Account", RunStagingStore.StagingTableName("Account"));
        Assert.Equal("in_csv1", RunStagingStore.InputTableName("csv1"));
    }

    [Fact]
    public void Enqueue_2000_Rows_Copies_All()
    {
        using var store = RunStagingStore.Create(_dir, "run-010");
        store.CreateStagingTable("Big", new[]
        {
            new StagingColumn("Id", StagingColumnType.Integer),
            new StagingColumn("Name", StagingColumnType.Text),
        });
        store.InsertStagingRows("Big", Enumerable.Range(0, 2000).Select(i => new object?[] { (long)i, "名前" + i }));

        var copied = store.EnqueueFromStaging("Big");
        Assert.Equal(2000, copied);

        var last = store.FetchQueueRows("Big", null, limit: 1, afterRowId: 1999);
        Assert.Single(last);
        Assert.Equal("名前1999", last[0].Values[1]);
    }
}
