using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class ExcelExporterTests
{
    private static string TempFile([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => Path.Combine(Path.GetTempPath(), $"sfui-export-{name}-{Guid.NewGuid():N}.xlsx");

    [Fact]
    public void Write_creates_workbook_with_sheets_and_roundtrips_values()
    {
        var path = TempFile();
        try
        {
            var sheets = new List<ExportSheet>
            {
                new()
                {
                    Name = "オブジェクト定義",
                    Columns = ["API 名", "ラベル", "カスタム"],
                    Rows = new List<IReadOnlyList<string?>>
                    {
                        new List<string?> { "Account", "取引先", "false" },
                        new List<string?> { "ACM_Setting__c", "設定, テスト", "true" },
                        new List<string?> { "Quote", "a\"b", "  空白付き  " },
                    },
                },
                new()
                {
                    Name = "項目_Account",
                    Columns = ["ラベル", "API 名"],
                    Rows = new List<IReadOnlyList<string?>>
                    {
                        new List<string?> { "従業員数", "NumberOfEmployees" },
                    },
                },
            };

            ExcelExporter.Write(path, sheets);

            Assert.True(File.Exists(path));

            using var document = SpreadsheetDocument.Open(path, false);
            var workbookPart = document.WorkbookPart!;

            var names = workbookPart.Workbook!.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value!).ToList();
            Assert.Equal(["オブジェクト定義", "項目_Account"], names);

            var firstSheet = workbookPart.WorksheetParts.First().Worksheet!;

            // ヘッダー行
            Assert.Equal("API 名", GetCellText(workbookPart, firstSheet, "A1"));
            Assert.Equal("カスタム", GetCellText(workbookPart, firstSheet, "C1"));

            // データ行（日本語・カンマ・引用符・前後空白）
            Assert.Equal("ACM_Setting__c", GetCellText(workbookPart, firstSheet, "A3"));
            Assert.Equal("設定, テスト", GetCellText(workbookPart, firstSheet, "B3"));
            Assert.Equal("a\"b", GetCellText(workbookPart, firstSheet, "B4"));
            Assert.Equal("  空白付き  ", GetCellText(workbookPart, firstSheet, "C4"));

            // ウィンドウ枠の固定とオートフィルター
            var pane = firstSheet.Descendants<Pane>().FirstOrDefault();
            Assert.NotNull(pane);
            Assert.Equal(PaneStateValues.FrozenSplit, pane.State!.Value);
            Assert.Equal("A1:C4", firstSheet.Elements<AutoFilter>().Single().Reference!.Value);

            // 2 シート目も読める
            var secondSheet = workbookPart.WorksheetParts.Last().Worksheet!;
            Assert.Equal("NumberOfEmployees", GetCellText(workbookPart, secondSheet, "B2"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Write_handles_newlines_emoji_and_control_characters()
    {
        var path = TempFile();
        try
        {
            var sheets = new List<ExportSheet>
            {
                new()
                {
                    Name = "特殊文字",
                    Columns = ["値"],
                    Rows = new List<IReadOnlyList<string?>>
                    {
                        new List<string?> { "1 行目\n2 行目" },
                        new List<string?> { "絵文字🙂OK" },
                        new List<string?> { "制御\u0001文字" },
                    },
                },
            };

            ExcelExporter.Write(path, sheets);

            using var document = SpreadsheetDocument.Open(path, false);
            var workbookPart = document.WorkbookPart!;
            var worksheet = workbookPart.WorksheetParts.First().Worksheet!;

            Assert.Equal("1 行目\n2 行目", GetCellText(workbookPart, worksheet, "A2"));
            Assert.Equal("絵文字🙂OK", GetCellText(workbookPart, worksheet, "A3"));
            Assert.Equal("制御文字", GetCellText(workbookPart, worksheet, "A4"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Write_sanitizes_and_deduplicates_sheet_names()
    {
        var path = TempFile();
        try
        {
            var longName = new string('A', 40);
            var sheets = new List<ExportSheet>
            {
                new() { Name = "項目_Account", Columns = ["x"] },
                new() { Name = "項目_Account", Columns = ["x"] },
                new() { Name = "a/b[c]:d*e?f\\g", Columns = ["x"] },
                new() { Name = longName, Columns = ["x"] },
            };

            ExcelExporter.Write(path, sheets);

            using var document = SpreadsheetDocument.Open(path, false);
            var names = document.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value!).ToList();

            Assert.Equal("項目_Account", names[0]);
            Assert.Equal("項目_Account (2)", names[1]);
            Assert.Equal("a_b_c__d_e_f_g", names[2]);
            Assert.Equal(31, names[3].Length);
            Assert.All(names, n => Assert.True(n.Length <= 31));
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SanitizeSheetName_applies_excel_rules()
    {
        Assert.Equal("Sheet", ExcelExporter.SanitizeSheetName(null, new HashSet<string>()));
        Assert.Equal("Sheet", ExcelExporter.SanitizeSheetName("   ", new HashSet<string>()));
        Assert.Equal("Sheet", ExcelExporter.SanitizeSheetName("''", new HashSet<string>()));

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("テスト", ExcelExporter.SanitizeSheetName("テスト", used));
        Assert.Equal("テスト (2)", ExcelExporter.SanitizeSheetName("テスト", used));
        Assert.Equal("テスト (3)", ExcelExporter.SanitizeSheetName("テスト", used));

        // 大文字小文字は同一視する（Excel の制約）
        var caseUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("abc", ExcelExporter.SanitizeSheetName("abc", caseUsed));
        Assert.Equal("ABC (2)", ExcelExporter.SanitizeSheetName("ABC", caseUsed));
    }

    [Fact]
    public void ToCsv_outputs_header_and_rows_with_rfc4180_escaping()
    {
        var sheet = new ExportSheet
        {
            Name = "CSV",
            Columns = ["A", "B"],
            Rows = new List<IReadOnlyList<string?>>
            {
                new List<string?> { "a,b", "c\"d" },
                new List<string?> { "日本語", null },
            },
        };

        var csv = sheet.ToCsv();

        Assert.Equal("A,B\r\n\"a,b\",\"c\"\"d\"\r\n日本語,\r\n", csv);
    }

    [Fact]
    public void Write_header_only_sheet_is_valid()
    {
        var path = TempFile();
        try
        {
            ExcelExporter.Write(path, [new ExportSheet { Name = "空", Columns = ["列 1", "列 2"] }]);

            using var document = SpreadsheetDocument.Open(path, false);
            var worksheet = document.WorkbookPart!.WorksheetParts.First().Worksheet!;
            var rows = worksheet.Descendants<Row>().ToList();
            Assert.Single(rows);
            Assert.Equal("列 1", GetCellText(document.WorkbookPart, worksheet, "A1"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string? GetCellText(WorkbookPart workbookPart, Worksheet worksheet, string reference)
    {
        var cell = worksheet.Descendants<Row>()
            .SelectMany(row => row.Elements<Cell>())
            .FirstOrDefault(c => c.CellReference?.Value == reference);
        if (cell is null)
        {
            return null;
        }

        if (cell.DataType?.Value == CellValues.SharedString)
        {
            var index = int.Parse(cell.CellValue!.Text, CultureInfo.InvariantCulture);
            var item = workbookPart.SharedStringTablePart!.SharedStringTable!.ElementAt(index);
            return item.InnerText;
        }

        return cell.CellValue?.Text;
    }
}
