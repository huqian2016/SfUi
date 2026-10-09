using System.Text;
using SfUi.Etl.Connections;
using Xunit;

namespace SfUi.Tests;

/// <summary>ストリーミング CSV リーダー（引用・エンコーディング・改行・区切り文字）を検証する。</summary>
public class CsvStreamReaderTests
{
    static CsvStreamReaderTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static string WriteTemp(string content, Encoding encoding)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sfui-csv-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content, encoding);
        return path;
    }

    [Fact]
    public void Reads_Header_And_Rows()
    {
        var path = WriteTemp("Id,Name,Amount\r\n1,Alpha,10\r\n2,Beta,20\r\n", new UTF8Encoding(false));
        try
        {
            using var reader = new CsvStreamReader(path);

            Assert.Equal(new[] { "Id", "Name", "Amount" }, reader.Headers);
            var rows = reader.ReadRows().ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { "1", "Alpha", "10" }, rows[0]);
            Assert.Equal(new[] { "2", "Beta", "20" }, rows[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Handles_Quoted_Comma_Quote_And_Newline()
    {
        var path = WriteTemp("A,B\n\"x, y\",\"say \"\"hi\"\"\"\n\"line1\nline2\",z\n", new UTF8Encoding(false));
        try
        {
            using var reader = new CsvStreamReader(path);
            var rows = reader.ReadRows().ToList();

            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { "x, y", "say \"hi\"" }, rows[0]);
            Assert.Equal(new[] { "line1\nline2", "z" }, rows[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Detects_ShiftJis()
    {
        var path = WriteTemp("名前,金額\n株式会社テスト,100\n", Encoding.GetEncoding(932));
        try
        {
            using var reader = new CsvStreamReader(path);
            var rows = reader.ReadRows().ToList();

            Assert.Equal(new[] { "名前", "金額" }, reader.Headers);
            Assert.Equal("株式会社テスト", rows[0][0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Detects_Utf8_Bom()
    {
        var path = WriteTemp("名前,金額\nテスト,100\n", new UTF8Encoding(true));
        try
        {
            using var reader = new CsvStreamReader(path);
            var rows = reader.ReadRows().ToList();

            Assert.Equal("名前", reader.Headers[0]);
            Assert.Equal("テスト", rows[0][0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Skips_Empty_Lines()
    {
        var path = WriteTemp("A\n1\n\n2\n\n", new UTF8Encoding(false));
        try
        {
            using var reader = new CsvStreamReader(path);
            var rows = reader.ReadRows().ToList();

            Assert.Equal(2, rows.Count);
            Assert.Equal("1", rows[0][0]);
            Assert.Equal("2", rows[1][0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Supports_Tsv_And_Custom_Delimiter()
    {
        var path = WriteTemp("A\tB\n1\t2\n", new UTF8Encoding(false));
        try
        {
            using var reader = new CsvStreamReader(path, new CsvStreamOptions { Delimiter = '\t' });

            Assert.Equal(new[] { "A", "B" }, reader.Headers);
            Assert.Equal(new[] { "1", "2" }, reader.ReadRows().Single());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WithoutHeader_Treats_All_Rows_As_Data()
    {
        var path = WriteTemp("1,2\n3,4\n", new UTF8Encoding(false));
        try
        {
            using var reader = new CsvStreamReader(path, new CsvStreamOptions { HasHeader = false });

            Assert.Empty(reader.Headers);
            var rows = reader.ReadRows().ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { "1", "2" }, rows[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Ragged_Rows_Are_Returned_AsIs()
    {
        var path = WriteTemp("A,B,C\n1,2\n1,2,3,4\n", new UTF8Encoding(false));
        try
        {
            using var reader = new CsvStreamReader(path);
            var rows = reader.ReadRows().ToList();

            Assert.Equal(2, rows[0].Length);
            Assert.Equal(4, rows[1].Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reads_10000_Rows()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,名前");
        for (var i = 0; i < 10_000; i++)
        {
            sb.Append(i).Append(",名前").Append(i).Append('\n');
        }

        var path = WriteTemp(sb.ToString(), new UTF8Encoding(false));
        try
        {
            using var reader = new CsvStreamReader(path);
            var count = 0;
            string? last = null;
            foreach (var row in reader.ReadRows())
            {
                count++;
                last = row[1];
            }

            Assert.Equal(10_000, count);
            Assert.Equal("名前9999", last);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
