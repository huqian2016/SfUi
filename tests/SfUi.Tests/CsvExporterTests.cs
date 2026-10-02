using System.Data;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class CsvExporterTests
{
    private static DataTable CreateTable()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Rows.Add("001", "Acme, Inc.");
        table.Rows.Add("002", "Say \"Hi\"");
        table.Rows.Add(DBNull.Value, "Line1\nLine2");
        return table;
    }

    [Fact]
    public void ToCsv_IncludesHeaderAndEscapesFields()
    {
        var csv = CsvExporter.ToCsv(CreateTable());

        Assert.StartsWith("Id,Name" + Environment.NewLine, csv);
        Assert.Contains("001,\"Acme, Inc.\"", csv);
        Assert.Contains("\"Say \"\"Hi\"\"\"", csv);
        Assert.Contains("\"Line1\nLine2\"", csv);
    }

    [Fact]
    public void ToCsv_EmptyTable_ReturnsHeaderOnly()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(string));

        var csv = CsvExporter.ToCsv(table);

        Assert.Equal("Id" + Environment.NewLine, csv);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("abc", "abc")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a\nb", "\"a\nb\"")]
    [InlineData("a\r\nb", "\"a\r\nb\"")]
    public void Escape_HandlesSpecialCharacters(string? input, string expected)
    {
        Assert.Equal(expected, CsvExporter.Escape(input));
    }
}
