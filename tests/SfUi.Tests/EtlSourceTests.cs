using System.IO.Compression;
using System.Text;
using SfUi.Core;
using SfUi.Etl.Connections;
using SfUi.Etl.Sources;
using Xunit;

namespace SfUi.Tests;

/// <summary>入力ソース（CSV / Excel / JSON / XML）を検証する。</summary>
public class EtlSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sfui-etl-src-" + Guid.NewGuid().ToString("N"));

    public EtlSourceTests() => Directory.CreateDirectory(_dir);

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

    private string TempFile(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Csv_WithHeader_ReadsColumnsAndRows()
    {
        var path = TempFile("data.csv");
        File.WriteAllText(path, "Id,名前,メモ\r\n1,太郎,\"a,b\"\r\n2,花子,\r\n", new UTF8Encoding(false));

        var source = new CsvFileSource(path);

        Assert.Equal(new[] { "Id", "名前", "メモ" }, source.Columns);
        var rows = source.ReadRows().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new object?[] { "1", "太郎", "a,b" }, rows[0]);
        Assert.Equal(new object?[] { "2", "花子", "" }, rows[1]);
        Assert.Equal("data.csv", source.Name);
    }

    [Fact]
    public void Csv_NoHeader_GeneratesColumnNames()
    {
        var path = TempFile("noheader.csv");
        File.WriteAllText(path, "1,太郎\r\n2,花子\r\n", new UTF8Encoding(false));

        var source = new CsvFileSource(path, new CsvStreamOptions { HasHeader = false });

        Assert.Equal(new[] { "Column1", "Column2" }, source.Columns);
        Assert.Equal(2, source.ReadRows().Count());
    }

    [Fact]
    public void Json_Array_UnionColumns()
    {
        var path = TempFile("data.json");
        File.WriteAllText(
            path,
            """[{"Name":"A","Qty":10},{"Name":"B","Extra":true},{"Name":"C","Nested":{"x":1}}]""",
            new UTF8Encoding(false));

        var source = new JsonFileSource(path);

        Assert.Equal(new[] { "Name", "Qty", "Extra", "Nested" }, source.Columns);
        var rows = source.ReadRows().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(new object?[] { "A", "10", null, null }, rows[0]);
        Assert.Equal(new object?[] { "B", null, "true", null }, rows[1]);
        Assert.Equal("""{"x":1}""", rows[2][3]);
    }

    [Fact]
    public void Json_ArrayProperty()
    {
        var path = TempFile("wrapped.json");
        File.WriteAllText(path, """{"records":[{"Id":"1"},{"Id":"2"}],"other":true}""", new UTF8Encoding(false));

        var source = new JsonFileSource(path, arrayProperty: "records");

        Assert.Equal(new[] { "Id" }, source.Columns);
        Assert.Equal(2, source.ReadRows().Count());
    }

    [Fact]
    public void Xml_RowsAndUnionColumns()
    {
        var path = TempFile("data.xml");
        File.WriteAllText(
            path,
            "<rows><row><Name>A</Name><Qty>1</Qty></row><row><Name>B</Name></row></rows>",
            new UTF8Encoding(false));

        var source = new XmlFileSource(path);

        Assert.Equal("row", source.RowElementName);
        Assert.Equal(new[] { "Name", "Qty" }, source.Columns);
        var rows = source.ReadRows().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new object?[] { "A", "1" }, rows[0]);
        Assert.Equal(new object?[] { "B", null }, rows[1]);
    }

    [Fact]
    public void Excel_ReadsHeaderNumbersDatesAndEmptyCells()
    {
        var path = TempFile("data.xlsx");
        WriteMinimalXlsx(path);

        var source = new ExcelFileSource(path);

        Assert.Equal(new[] { "Name", "Age", "Joined", "Note" }, source.Columns);

        var rows = source.ReadRows().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("太郎", rows[0][0]);
        Assert.Equal(30d, rows[0][1]);
        Assert.Equal(new DateTime(1899, 12, 30).AddDays(45996), rows[0][2]);
        Assert.Null(rows[0][3]);
        Assert.Equal("花子", rows[1][0]);
        Assert.Equal(28.5d, rows[1][1]);
        Assert.Equal("メモ", rows[1][3]);
    }

    [Fact]
    public void Excel_ParsesWorkbookWrittenByExcelExporter()
    {
        // スモークの [inputs] と同じ経路: ExcelExporter で生成 → ExcelFileSource で読む
        var path = TempFile("exported.xlsx");
        ExcelExporter.Write(path, new[]
        {
            new ExportSheet
            {
                Name = "Contacts",
                Columns = new[] { "LastName", "Email" },
                Rows = new IReadOnlyList<string?>[]
                {
                    new[] { "SfUiInXl_1", "xl1@example.com" },
                    new[] { "SfUiInXl_2", "xl2@example.com" },
                },
            },
        });

        var source = new ExcelFileSource(path);

        Assert.Equal(new[] { "LastName", "Email" }, source.Columns);
        var rows = source.ReadRows().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new object?[] { "SfUiInXl_1", "xl1@example.com" }, rows[0]);
        Assert.Equal(new object?[] { "SfUiInXl_2", "xl2@example.com" }, rows[1]);
    }

    [Fact]
    public void SourceColumnNames_Normalize()
    {
        var names = SourceColumnNames.Normalize(new string?[] { "A", "", "A", " A ", null });

        Assert.Equal(new[] { "A", "Column2", "A_2", "A_3", "Column5" }, names);
    }

    /// <summary>テスト用の最小 xlsx（inline 文字列・数値・日付スタイル・空セル）を生成する。</summary>
    private static void WriteMinimalXlsx(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        Add(zip, "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """);

        Add(zip, "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

        Add(zip, "xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);

        Add(zip, "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);

        Add(zip, "xl/styles.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fonts count="1"><font/></fonts>
              <fills count="1"><fill/></fills>
              <borders count="1"><border/></borders>
              <cellStyleXfs count="1"><xf/></cellStyleXfs>
              <cellXfs count="2"><xf numFmtId="0"/><xf numFmtId="14" applyNumberFormat="1"/></cellXfs>
            </styleSheet>
            """);

        Add(zip, "xl/worksheets/sheet1.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="inlineStr"><is><t>Name</t></is></c>
                  <c r="B1" t="inlineStr"><is><t>Age</t></is></c>
                  <c r="C1" t="inlineStr"><is><t>Joined</t></is></c>
                  <c r="D1" t="inlineStr"><is><t>Note</t></is></c>
                </row>
                <row r="2">
                  <c r="A2" t="inlineStr"><is><t>太郎</t></is></c>
                  <c r="B2"><v>30</v></c>
                  <c r="C2" s="1"><v>45996</v></c>
                </row>
                <row r="3">
                  <c r="A3" t="inlineStr"><is><t>花子</t></is></c>
                  <c r="B3"><v>28.5</v></c>
                  <c r="C3" s="1"><v>45997</v></c>
                  <c r="D3" t="inlineStr"><is><t>メモ</t></is></c>
                </row>
              </sheetData>
            </worksheet>
            """);
    }

    private static void Add(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
