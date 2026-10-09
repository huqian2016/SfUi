using System.Globalization;
using System.Text;
using SfUi.Etl.Database;
using SfUi.Etl.Engine;
using SfUi.Etl.Expressions;
using SfUi.Etl.Sources;
using SfUi.Etl.Staging;
using SfUi.Etl.Transforms;
using Xunit;

namespace SfUi.Tests;

/// <summary>DB コネクタ（SQLite で検証: ソース / insert・upsert・delete + journal + 巻き戻し）。</summary>
public class DbTableTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-etl-db-" + Guid.NewGuid().ToString("N"));

    public DbTableTests() => Directory.CreateDirectory(_dir);

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

    private DbConnectionSpec CreateSpec(string name)
        => new(DbProviderKind.Sqlite, "Data Source=" + Path.Combine(_dir, name + ".db"));

    private static async Task ExecuteAsync(DbConnectionSpec spec, string sql)
    {
        await using var connection = spec.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(DbConnectionSpec spec, string sql)
    {
        await using var connection = spec.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<object?> ScalarValueAsync(DbConnectionSpec spec, string sql)
    {
        await using var connection = spec.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static QueueRow Row(long id, params object?[] values)
        => new(id, values, RowOp.Insert, QueueStatus.Pending, 0, null, null, null);

    private static EtlApplyContext Context(RunStagingStore store, string table)
        => new() { Store = store, StepId = "step1", ObjectName = table };

    [Fact]
    public void QuoteIdentifier_AllProviders()
    {
        Assert.Equal("\"x\"", DbConnectionSpec.QuoteIdentifier(DbProviderKind.Sqlite, "x"));
        Assert.Equal("[My Table]", DbConnectionSpec.QuoteIdentifier(DbProviderKind.SqlServer, "My Table"));
        Assert.Equal("[a]]b]", DbConnectionSpec.QuoteIdentifier(DbProviderKind.SqlServer, "a]b"));
        Assert.Equal("\"a\"\"b\"", DbConnectionSpec.QuoteIdentifier(DbProviderKind.PostgreSql, "a\"b"));
        Assert.Equal("\"x\"", DbConnectionSpec.QuoteIdentifier(DbProviderKind.Odbc, "x"));
    }

    [Fact]
    public void Source_ReadsColumnsAndRows()
    {
        var spec = CreateSpec("source");
        ExecuteAsync(spec, "CREATE TABLE people (Name TEXT, Age INTEGER);").GetAwaiter().GetResult();
        ExecuteAsync(spec, "INSERT INTO people VALUES ('Alice', 30), ('Bob', 25);").GetAwaiter().GetResult();

        var source = new DbTableSource(spec, "SELECT Name, Age FROM people ORDER BY Age");

        Assert.Equal(new[] { "Name", "Age" }, source.Columns);
        var rows = source.ReadRows().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Bob", rows[0][0]);
        Assert.Equal(25L, rows[0][1]);
        Assert.Equal("Alice", rows[1][0]);
    }

    [Fact]
    public async Task Insert_AddsRowsAndJournal()
    {
        var spec = CreateSpec("insert");
        await ExecuteAsync(spec, "CREATE TABLE contacts (Name TEXT, Age INTEGER);");
        using var store = RunStagingStore.Create(_dir, "db-insert");

        var target = new DbTableTarget(spec, new DbTableTargetOptions
        {
            Table = "contacts",
            Fields = new[] { "Name", "Age" },
            Op = RowOp.Insert,
            KeyField = "Name",
        });

        var result = await target.ApplyBatchAsync(Context(store, "contacts"), new[]
        {
            Row(1, "Alice", 30L),
            Row(2, "Bob", 20L),
        }, default);

        Assert.All(result.Rows, r => Assert.True(r.Success));
        Assert.Equal("Alice", result.Rows[0].TargetId);
        Assert.Equal(2L, await ScalarAsync(spec, "SELECT COUNT(*) FROM contacts"));

        var journal = store.QueryJournal("contacts");
        Assert.Equal(2, journal.Count);
        Assert.All(journal, j =>
        {
            Assert.Equal(RowOp.Insert, j.Op);
            Assert.True(j.Id > 0);
        });
        Assert.Equal("Alice", journal[0].TargetId);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingAndInsertsNew()
    {
        var spec = CreateSpec("upsert");
        await ExecuteAsync(spec, "CREATE TABLE items (Name TEXT, Qty INTEGER);");
        await ExecuteAsync(spec, "INSERT INTO items VALUES ('Alice', 30);");
        using var store = RunStagingStore.Create(_dir, "db-upsert");

        var target = new DbTableTarget(spec, new DbTableTargetOptions
        {
            Table = "items",
            Fields = new[] { "Name", "Qty" },
            Op = RowOp.Upsert,
            KeyField = "Name",
        });

        var result = await target.ApplyBatchAsync(Context(store, "items"), new[]
        {
            Row(1, "Alice", 31L),
            Row(2, "Bob", 20L),
        }, default);

        Assert.All(result.Rows, r => Assert.True(r.Success));
        Assert.Equal(31L, await ScalarAsync(spec, "SELECT Qty FROM items WHERE Name = 'Alice'"));
        Assert.Equal(20L, await ScalarAsync(spec, "SELECT Qty FROM items WHERE Name = 'Bob'"));

        var journal = store.QueryJournal("items");
        Assert.Equal(2, journal.Count);
        Assert.Contains(journal, j => j.Op == RowOp.Update && j.BeforeJson!.Contains("30", StringComparison.Ordinal));
        Assert.Contains(journal, j => j.Op == RowOp.Insert);
    }

    [Fact]
    public async Task Update_MissingKey_Fails()
    {
        var spec = CreateSpec("update-miss");
        await ExecuteAsync(spec, "CREATE TABLE items (Name TEXT, Qty INTEGER);");
        using var store = RunStagingStore.Create(_dir, "db-update-miss");

        var target = new DbTableTarget(spec, new DbTableTargetOptions
        {
            Table = "items",
            Fields = new[] { "Name", "Qty" },
            Op = RowOp.Update,
            KeyField = "Name",
        });

        var result = await target.ApplyBatchAsync(Context(store, "items"), new[] { Row(1, "Nobody", 1L) }, default);

        Assert.False(result.Rows[0].Success);
        Assert.Contains("見つかりません", result.Rows[0].Error);
    }

    [Fact]
    public async Task Delete_RemovesRowAndJournalHasBeforeImage()
    {
        var spec = CreateSpec("delete");
        await ExecuteAsync(spec, "CREATE TABLE items (Name TEXT, Qty INTEGER);");
        await ExecuteAsync(spec, "INSERT INTO items VALUES ('Bob', 20);");
        using var store = RunStagingStore.Create(_dir, "db-delete");

        var target = new DbTableTarget(spec, new DbTableTargetOptions
        {
            Table = "items",
            Fields = new[] { "Name", "Qty" },
            Op = RowOp.Delete,
            KeyField = "Name",
        });

        var result = await target.ApplyBatchAsync(Context(store, "items"), new[] { Row(1, "Bob", 20L) }, default);

        Assert.True(result.Rows[0].Success);
        Assert.Equal(0L, await ScalarAsync(spec, "SELECT COUNT(*) FROM items"));
        var journal = store.QueryJournal("items");
        Assert.Single(journal);
        Assert.Equal(RowOp.Delete, journal[0].Op);
        Assert.Contains("Bob", journal[0].BeforeJson);
    }

    [Fact]
    public async Task Revert_AllOperations_RestoresTable()
    {
        var spec = CreateSpec("revert");
        await ExecuteAsync(spec, "CREATE TABLE items (Name TEXT, Qty INTEGER);");
        using var store = RunStagingStore.Create(_dir, "db-revert");

        var upsert = new DbTableTarget(spec, new DbTableTargetOptions
        {
            Table = "items",
            Fields = new[] { "Name", "Qty" },
            Op = RowOp.Upsert,
            KeyField = "Name",
        });
        var delete = new DbTableTarget(spec, new DbTableTargetOptions
        {
            Table = "items",
            Fields = new[] { "Name", "Qty" },
            Op = RowOp.Delete,
            KeyField = "Name",
        });
        var context = Context(store, "items");

        await upsert.ApplyBatchAsync(context, new[] { Row(1, "Alice", 30L) }, default);
        await upsert.ApplyBatchAsync(context, new[] { Row(2, "Alice", 31L) }, default);
        await upsert.ApplyBatchAsync(context, new[] { Row(3, "Bob", 20L) }, default);
        await delete.ApplyBatchAsync(context, new[] { Row(4, "Bob", 20L) }, default);

        Assert.Equal(31L, await ScalarAsync(spec, "SELECT Qty FROM items WHERE Name = 'Alice'"));
        Assert.Equal(0L, await ScalarAsync(spec, "SELECT COUNT(*) FROM items WHERE Name = 'Bob'"));

        var reverter = new JournalReverter(store, upsert, "step1", "items");
        var result = await reverter.RevertAsync();

        Assert.Equal(4, result.Reverted);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0L, await ScalarAsync(spec, "SELECT COUNT(*) FROM items"));

        var journal = store.QueryJournal("items");
        Assert.All(journal, j => Assert.Equal("reverted", j.RevertStatus));
    }

    [Fact]
    public async Task StepRun_CsvToSqlite_EndToEnd()
    {
        var spec = CreateSpec("e2e");
        await ExecuteAsync(spec, "CREATE TABLE people (Name TEXT, Age INTEGER);");
        var csvPath = Path.Combine(_dir, "people.csv");
        File.WriteAllText(csvPath, "Name,Age\r\nAlice,30\r\nBob,20\r\n", new UTF8Encoding(false));

        using var store = RunStagingStore.Create(_dir, "db-e2e");
        var source = new CsvFileSource(csvPath);
        var mapper = new RowMapper(source.Columns, new[]
        {
            new FieldMapping("Name", "[Name]"),
            new FieldMapping("Age", "[Age]", StagingColumnType.Integer),
        }, new ExpressionEngine());
        var target = new DbTableTarget(spec, new DbTableTargetOptions
        {
            Table = "people",
            Fields = new[] { "Name", "Age" },
            Op = RowOp.Insert,
            KeyField = "Name",
        });
        var plan = new EtlStepPlan
        {
            StepId = "step1",
            ObjectName = "people",
            Source = source,
            Mapper = mapper,
            Target = target,
            Revertable = target,
        };

        var step = new EtlStepRun(store, plan);
        var result = await step.RunAsync();

        Assert.Equal(2, result.Apply!.Success);
        Assert.Equal(2L, await ScalarAsync(spec, "SELECT COUNT(*) FROM people"));
        Assert.Equal("Alice", await ScalarValueAsync(spec, "SELECT Name FROM people ORDER BY Age DESC"));

        var rollback = await step.RollbackAsync();
        Assert.Equal(2, rollback.Reverted);
        Assert.Equal(0L, await ScalarAsync(spec, "SELECT COUNT(*) FROM people"));
    }

    [Fact]
    public async Task TestAsync_MissingTable_ReturnsFalse()
    {
        var spec = CreateSpec("missing");
        var target = new DbTableTarget(spec, new DbTableTargetOptions
        {
            Table = "missing",
            Fields = new[] { "Name" },
            Op = RowOp.Insert,
        });

        Assert.False(await target.TestAsync(default));
    }

    [Fact]
    public void KeyField_NotInFields_Throws()
    {
        var spec = CreateSpec("invalid");
        Assert.Throws<ArgumentException>(() => new DbTableTarget(spec, new DbTableTargetOptions
        {
            Table = "items",
            Fields = new[] { "Name" },
            Op = RowOp.Upsert,
            KeyField = "Missing",
        }));

        Assert.Throws<ArgumentException>(() => new DbTableTarget(spec, new DbTableTargetOptions
        {
            Table = "items",
            Fields = new[] { "Name" },
            Op = RowOp.Delete,
        }));
    }
}
