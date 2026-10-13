using System.Data;
using SfUi.App.ViewModels;
using Xunit;

namespace SfUi.Tests;

/// <summary>SOQL 結果グリッド モデル（Id 先頭固定・読み取り専用判定・編集状態）のテスト。</summary>
public class SoqlResultModelBuilderTests
{
    private static DataTable Table(params string[] columns)
    {
        var table = new DataTable();
        foreach (var column in columns)
        {
            table.Columns.Add(column, typeof(string));
        }

        return table;
    }

    [Fact]
    public void Build_PutsIdFirstAndMarksItReadOnly()
    {
        var table = Table("Name", "Id", "Industry");
        table.Rows.Add("Acme", "001XX", "IT");

        var (columns, rows) = SoqlResultModelBuilder.Build(table);

        Assert.Equal(new[] { "Id", "Name", "Industry" }, columns.Select(c => c.Key));
        Assert.True(columns[0].IsId);
        Assert.True(columns[0].IsReadOnly);
        Assert.Equal("001XX", rows[0].RecordId);
        Assert.False(columns[1].IsReadOnly);
        Assert.Equal("Acme", rows[0].Cells[1].Value);
    }

    [Fact]
    public void Build_MarksSystemFieldsAndRelationshipsReadOnly()
    {
        var table = Table("Id", "Name", "CreatedDate", "Account.Name", "IsDeleted", "LastModifiedDate");
        table.Rows.Add("001XX", "Acme", "2026-01-01", "Acme", "false", "2026-01-02");

        var (columns, _) = SoqlResultModelBuilder.Build(table);

        Assert.False(columns.Single(c => c.Key == "Name").IsReadOnly);
        Assert.True(columns.Single(c => c.Key == "CreatedDate").IsReadOnly);
        Assert.True(columns.Single(c => c.Key == "Account.Name").IsReadOnly);
        Assert.True(columns.Single(c => c.Key == "IsDeleted").IsReadOnly);
        Assert.True(columns.Single(c => c.Key == "LastModifiedDate").IsReadOnly);
    }

    [Fact]
    public void Build_MarksJsonArrayColumnsReadOnly()
    {
        var table = Table("Id", "Name", "Contacts");
        table.Rows.Add("001XX", "Acme", "[{\"Id\":\"003XX\"}]");

        var (columns, _) = SoqlResultModelBuilder.Build(table);

        Assert.True(columns.Single(c => c.Key == "Contacts").IsReadOnly);
    }

    [Fact]
    public void Build_WithoutIdMakesEverythingReadOnly()
    {
        var table = Table("Name", "TotalAmount");
        table.Rows.Add("Acme", "100");

        var (columns, rows) = SoqlResultModelBuilder.Build(table);

        Assert.All(columns, c => Assert.True(c.IsReadOnly));
        Assert.Null(rows[0].RecordId);
    }

    [Fact]
    public void Build_KeepsNullCellsAsNull()
    {
        var table = Table("Id", "Name");
        table.Rows.Add("001XX", DBNull.Value);

        var (_, rows) = SoqlResultModelBuilder.Build(table);

        Assert.Null(rows[0].Cells[1].Value);
    }

    [Fact]
    public void Cell_TracksDirtyStateAndCommit()
    {
        var cell = new SoqlCell("before", isReadOnly: false);

        Assert.False(cell.IsDirty);
        Assert.Equal(0, cell.StyleState);

        cell.Value = "after";
        Assert.True(cell.IsDirty);
        Assert.Equal(1, cell.StyleState);

        cell.Error = "boom";
        Assert.True(cell.HasError);
        Assert.Equal(2, cell.StyleState);

        cell.Commit();
        Assert.False(cell.IsDirty);
        Assert.Null(cell.Error);
        Assert.Equal("after", cell.OriginalValue);
        Assert.Equal(0, cell.StyleState);

        cell.Value = "after";
        Assert.False(cell.IsDirty);
    }

    [Fact]
    public void Cell_ReadOnlyNeverDirty()
    {
        var cell = new SoqlCell("id", isReadOnly: true);
        cell.Value = "changed";

        Assert.False(cell.IsDirty);
        Assert.Equal(0, cell.StyleState);
    }
}
