using System.Data;
using SfUi.App.ViewModels;
using Xunit;

namespace SfUi.Tests;

/// <summary>
/// 複数 SOQL 実行時の結果枠（<see cref="SoqlResultPaneViewModel"/>）の絞り込み・状態表示のテスト。
/// </summary>
[Collection("Localization")]
public class SoqlResultPaneViewModelTests
{
    private static DataTable CreateTable(params (string Id, string Name, string Industry)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Industry", typeof(string));
        foreach (var row in rows)
        {
            table.Rows.Add(row.Id, row.Name, row.Industry);
        }

        return table;
    }

    private static SoqlResultPaneViewModel CreatePaneWithRows()
    {
        var pane = new SoqlResultPaneViewModel(0, "SELECT Id, Name, Industry FROM Account");
        pane.SetResult(
            CreateTable(
                ("001A", "Acme", "Manufacturing"),
                ("001B", "Globex", "Retail"),
                ("001C", "Initech", "Software")),
            "ok");
        return pane;
    }

    [Fact]
    public void Title_IncludesStatementNumberAndQuery()
    {
        var pane = new SoqlResultPaneViewModel(1, "SELECT   Id\nFROM Contact");

        Assert.Equal(1, pane.Index);
        Assert.Contains("SELECT Id FROM Contact", pane.Title);
    }

    [Fact]
    public void SetResult_FillsAllRowsAndVisibleRows()
    {
        var pane = CreatePaneWithRows();

        Assert.Equal(3, pane.Columns.Count);
        Assert.Equal(3, pane.Rows.Count);
        Assert.Equal(3, pane.VisibleRows.Count);
        Assert.False(pane.HasError);
        Assert.Equal("ok", pane.StatusText);
    }

    [Fact]
    public void Filter_MatchesAnyCellCaseInsensitively()
    {
        var pane = CreatePaneWithRows();

        pane.FilterText = "glob";

        Assert.Single(pane.VisibleRows);
        Assert.Equal("001B", pane.VisibleRows[0].RecordId);
        Assert.Equal(3, pane.Rows.Count);
    }

    [Fact]
    public void Filter_MatchesRecordId()
    {
        var pane = CreatePaneWithRows();

        pane.FilterText = "001C";

        Assert.Single(pane.VisibleRows);
        Assert.Equal("Initech", pane.VisibleRows[0].Cells[1].Value);
    }

    [Fact]
    public void Filter_Cleared_RestoresAllRows()
    {
        var pane = CreatePaneWithRows();

        pane.FilterText = "software";
        Assert.Single(pane.VisibleRows);

        pane.FilterText = string.Empty;
        Assert.Equal(3, pane.VisibleRows.Count);
    }

    [Fact]
    public void Filter_NoMatch_YieldsEmptyVisibleRowsAndKeepsRows()
    {
        var pane = CreatePaneWithRows();

        pane.FilterText = "zzz";

        Assert.Empty(pane.VisibleRows);
        Assert.Equal(3, pane.Rows.Count);
    }

    [Fact]
    public void Error_MarksPaneAndKeepsCachedRows()
    {
        var pane = new SoqlResultPaneViewModel(0, "SELECT Id FROM Account");
        pane.SetCached(CreateTable(("001A", "Acme", "Manufacturing")), DateTimeOffset.Now);

        Assert.True(pane.IsCachedResult);
        pane.SetError("MALFORMED_QUERY");

        Assert.True(pane.HasError);
        Assert.Equal("MALFORMED_QUERY", pane.ErrorText);
        Assert.Single(pane.VisibleRows);
        Assert.Contains("MALFORMED_QUERY", pane.StatusText);
    }

    [Fact]
    public void SetResult_ClearsCachedBadge()
    {
        var pane = new SoqlResultPaneViewModel(0, "SELECT Id FROM Account");
        pane.SetCached(CreateTable(("001A", "Acme", "Manufacturing")), DateTimeOffset.Now);

        pane.SetResult(CreateTable(("001A", "Acme", "Manufacturing"), ("001B", "Globex", "Retail")), "fresh");

        Assert.False(pane.IsCachedResult);
        Assert.Empty(pane.CachedResultText);
        Assert.Equal(2, pane.VisibleRows.Count);
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var pane = CreatePaneWithRows();

        pane.Clear();

        Assert.Empty(pane.Columns);
        Assert.Empty(pane.Rows);
        Assert.Empty(pane.VisibleRows);
        Assert.False(pane.HasError);
    }

    [Fact]
    public void ToTable_ReturnsVisibleRowsOnly()
    {
        var pane = CreatePaneWithRows();
        pane.FilterText = "retail";

        var table = pane.ToTable();

        Assert.NotNull(table);
        Assert.Single(table!.Rows);
        Assert.Equal("Globex", table.Rows[0]["Name"]);
    }
}
