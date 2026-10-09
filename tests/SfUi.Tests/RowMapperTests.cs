using SfUi.Etl.Expressions;
using SfUi.Etl.Staging;
using SfUi.Etl.Transforms;
using Xunit;

namespace SfUi.Tests;

/// <summary>マッピング変換（式 → 型変換 →ステージング テーブル）を検証する。</summary>
public class RowMapperTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-etl-map-" + Guid.NewGuid().ToString("N"));

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
    public void BasicMapping_RenameExpressionConstant()
    {
        var mapper = new RowMapper(new[] { "姓", "名" }, new[]
        {
            new FieldMapping("FirstName", "[姓]"),
            new FieldMapping("LastName", "[名]"),
            new FieldMapping("Full", "CONCAT([姓], \" \", [名])"),
            new FieldMapping("Type", "\"固定\""),
        }, new ExpressionEngine());

        var row = mapper.MapRow(new object?[] { "太郎", "山田" });

        Assert.Equal(new object?[] { "太郎", "山田", "太郎 山田", "固定" }, row);
    }

    [Fact]
    public void TypeConversions()
    {
        var mapper = new RowMapper(new[] { "A", "B", "C", "D", "E", "F" }, new[]
        {
            new FieldMapping("Int1", "[A]", StagingColumnType.Integer),
            new FieldMapping("Real1", "[B]", StagingColumnType.Real),
            new FieldMapping("Bool1", "[C]", StagingColumnType.Boolean),
            new FieldMapping("Bool2", "[D]", StagingColumnType.Boolean),
            new FieldMapping("Date1", "[E]", StagingColumnType.DateTime),
            new FieldMapping("Text1", "[F]", StagingColumnType.Text),
        }, new ExpressionEngine());

        var row = mapper.MapRow(new object?[] { "1,234", "1.5", "TRUE", "no", "2026/10/09", 123 });

        Assert.Equal(1234L, row[0]);
        Assert.Equal(1.5d, row[1]);
        Assert.Equal(true, row[2]);
        Assert.Equal(false, row[3]);
        Assert.Equal(new DateTime(2026, 10, 9), row[4]);
        Assert.Equal("123", row[5]);

        // 変換不能 → null
        var bad = mapper.MapRow(new object?[] { "abc", "x", "maybe", null, "not a date", null });
        Assert.Null(bad[0]);
        Assert.Null(bad[1]);
        Assert.Null(bad[2]);
        Assert.Null(bad[3]);
        Assert.Null(bad[4]);
        Assert.Null(bad[5]);
    }

    [Fact]
    public void PrevAndRowNumber()
    {
        var mapper = new RowMapper(new[] { "Amt" }, new[]
        {
            new FieldMapping("Cur", "[Amt]", StagingColumnType.Integer),
            new FieldMapping("PrevAmt", "PREV(\"Amt\")", StagingColumnType.Integer),
            new FieldMapping("No", "ROW_NUMBER()", StagingColumnType.Integer),
        }, new ExpressionEngine());

        var r1 = mapper.MapRow(new object?[] { "100" });
        var r2 = mapper.MapRow(new object?[] { "200" });
        var r3 = mapper.MapRow(new object?[] { "300" });

        Assert.Equal(100L, r1[0]);
        Assert.Null(r1[1]);
        Assert.Equal(1L, r1[2]);

        Assert.Equal(200L, r2[0]);
        Assert.Equal(100L, r2[1]);
        Assert.Equal(2L, r2[2]);

        Assert.Equal(300L, r3[0]);
        Assert.Equal(200L, r3[1]);
        Assert.Equal(3L, r3[2]);
    }

    [Fact]
    public void StagingColumns_Exposed()
    {
        var mapper = new RowMapper(new[] { "Name" }, new[]
        {
            new FieldMapping("Name", "[Name]"),
            new FieldMapping("Count", "1", StagingColumnType.Integer),
        }, new ExpressionEngine());

        Assert.Equal(new[]
        {
            new StagingColumn("Name", StagingColumnType.Text),
            new StagingColumn("Count", StagingColumnType.Integer),
        }, mapper.StagingColumns);
    }

    [Fact]
    public void MappedRows_InsertIntoStagingStore_AndEnqueue()
    {
        var engine = new ExpressionEngine();
        var mapper = new RowMapper(new[] { "姓", "名", "年齢" }, new[]
        {
            new FieldMapping("FirstName", "[姓]"),
            new FieldMapping("LastName", "[名]"),
            new FieldMapping("Full", "CONCAT([姓], \" \", [名])"),
            new FieldMapping("Age", "[年齢]", StagingColumnType.Integer),
        }, engine);

        using var store = RunStagingStore.Create(_dir, "run-map-1");
        store.CreateStagingTable("Contact", mapper.StagingColumns);

        var sourceRows = new[]
        {
            new object?[] { "太郎", "山田", "30" },
            new object?[] { "花子", "鈴木", "28" },
        };
        store.InsertStagingRows("Contact", mapper.MapAll(sourceRows));

        var rows = store.ReadStagingRows("Contact").ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("太郎 山田", rows[0][2]);
        Assert.Equal(30L, rows[0][3]);
        Assert.Equal("花子 鈴木", rows[1][2]);

        var copied = store.EnqueueFromStaging("Contact");
        Assert.Equal(2, copied);
        Assert.Equal(2, store.CountQueueByStatus("Contact")[QueueStatus.Pending]);
    }

    [Fact]
    public void LookupExpression_UsesHost()
    {
        var engine = new ExpressionEngine(new ExpressionHost
        {
            Lookup = (obj, key, value) => "001" + value,
        });
        var mapper = new RowMapper(new[] { "ParentExtId" }, new[]
        {
            new FieldMapping("AccountId", "LOOKUP(\"Account\", \"ExtId\", [ParentExtId])"),
        }, engine);

        var row = mapper.MapRow(new object?[] { "X9" });

        Assert.Equal("001X9", row[0]);
    }
}
