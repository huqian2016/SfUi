using System.Text;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class CsvParserTests
{
    private static byte[] ShiftJis(string text)
    {
        // CsvParser の静的コンストラクター（CodePages プロバイダー登録）を先に走らせる
        _ = CsvParser.Parse(string.Empty);
        return Encoding.GetEncoding(932).GetBytes(text);
    }

    [Fact]
    public void Parse_BasicTable()
    {
        var table = CsvParser.Parse("Id,Name\n001,Acme\n002,Globex");

        Assert.Equal(new[] { "Id", "Name" }, table.Headers);
        Assert.Equal(2, table.RowCount);
        Assert.Equal(new[] { "001", "Acme" }, table.Rows[0]);
        Assert.Equal(new[] { "002", "Globex" }, table.Rows[1]);
    }

    [Fact]
    public void Parse_QuotedFields_CommaQuoteAndNewline()
    {
        var table = CsvParser.Parse("A,B\n\"x,y\",\"say \"\"hi\"\"\"\n\"line1\nline2\",z");

        Assert.Equal(2, table.RowCount);
        Assert.Equal(new[] { "x,y", "say \"hi\"" }, table.Rows[0]);
        Assert.Equal(new[] { "line1\nline2", "z" }, table.Rows[1]);
    }

    [Fact]
    public void Parse_CrlfAndTrailingNewline()
    {
        var table = CsvParser.Parse("Id,Name\r\n001,Acme\r\n");

        Assert.Equal(1, table.RowCount);
        Assert.Equal(new[] { "001", "Acme" }, table.Rows[0]);
    }

    [Fact]
    public void Parse_SkipsBlankLines()
    {
        var table = CsvParser.Parse("Id\n\n001\n\n002\n");

        Assert.Equal(2, table.RowCount);
    }

    [Fact]
    public void Parse_EmptyText_ReturnsEmpty()
    {
        var table = CsvParser.Parse(string.Empty);

        Assert.Empty(table.Headers);
        Assert.Equal(0, table.RowCount);
    }

    [Fact]
    public void Parse_HeaderOnly()
    {
        var table = CsvParser.Parse("Id,Name\n");

        Assert.Equal(new[] { "Id", "Name" }, table.Headers);
        Assert.Equal(0, table.RowCount);
    }

    [Fact]
    public void Decode_Utf8Bom_IsStripped()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("名前,値")).ToArray();

        var (text, encoding) = CsvParser.Decode(bytes);

        Assert.Equal("名前,値", text);
        Assert.Equal("UTF-8 (BOM)", encoding);
    }

    [Fact]
    public void Decode_Utf8WithoutBom_UsesUtf8()
    {
        var (text, encoding) = CsvParser.Decode(Encoding.UTF8.GetBytes("名前,値"));

        Assert.Equal("名前,値", text);
        Assert.Equal("UTF-8", encoding);
    }

    [Fact]
    public void Decode_ShiftJis_FallsBack()
    {
        var (text, encoding) = CsvParser.Decode(ShiftJis("名前,値"));

        Assert.Equal("名前,値", text);
        Assert.Equal("Shift-JIS", encoding);
    }

    [Fact]
    public void Decode_ExplicitShiftJis()
    {
        var (text, encoding) = CsvParser.Decode(ShiftJis("名前,値"), CsvParser.EncodingShiftJis);

        Assert.Equal("名前,値", text);
        Assert.Equal("Shift-JIS", encoding);
    }

    [Fact]
    public void Decode_ExplicitUtf8()
    {
        var (text, encoding) = CsvParser.Decode(Encoding.UTF8.GetBytes("abc"), CsvParser.EncodingUtf8);

        Assert.Equal("abc", text);
        Assert.Equal("UTF-8", encoding);
    }

    [Fact]
    public void ReadFile_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "sfui-csv-test-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            File.WriteAllText(path, "Id,Name\n001,名前", new UTF8Encoding(true));

            var (text, encoding) = CsvParser.ReadFile(path);
            var table = CsvParser.Parse(text);

            Assert.Equal("UTF-8 (BOM)", encoding);
            Assert.Equal("名前", table.Rows[0][1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
