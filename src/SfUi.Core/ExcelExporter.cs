using System.Globalization;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace SfUi.Core;

/// <summary>
/// <see cref="ExportSheet"/> の一覧から xlsx（1 ブック複数シート）を生成する。
/// - ヘッダー行は太字 + 背景色、ウィンドウ枠固定、オートフィルター付き
/// - 列幅は内容から自動計算（CJK は 2 文字幅で計算、最大 60）
/// - すべての値は文字列として書き出す（xlsx と CSV の内容を一致させるため）
/// </summary>
public static class ExcelExporter
{
    private const int MaxSheetNameLength = 31;
    private const int MaxCellTextLength = 32767;
    private const double MaxColumnWidth = 60;
    private const double MinColumnWidth = 8;

    /// <summary>Excel がシート名に使えない文字。</summary>
    private static readonly char[] InvalidSheetNameChars = ['[', ']', ':', '*', '?', '/', '\\'];

    public static void Write(string filePath, IReadOnlyList<ExportSheet> sheets)
    {
        if (sheets.Count == 0)
        {
            sheets = [new ExportSheet { Name = "Sheet", Columns = [] }];
        }

        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        using var document = SpreadsheetDocument.Create(fullPath, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = BuildStylesheet();
        stylesPart.Stylesheet.Save();

        var shared = new SharedStringContext();
        var sharedPart = workbookPart.AddNewPart<SharedStringTablePart>();
        sharedPart.SharedStringTable = shared.Table;

        var sheetsElement = new Sheets();
        workbookPart.Workbook.AppendChild(sheetsElement);

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint sheetId = 1;
        foreach (var sheet in sheets)
        {
            var name = SanitizeSheetName(sheet.Name, usedNames);
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = BuildWorksheet(sheet, shared);
            worksheetPart.Worksheet.Save();

            sheetsElement.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = name,
            });
        }

        if (shared.TotalReferences > 0)
        {
            shared.Table.Count = (uint)shared.TotalReferences;
            shared.Table.UniqueCount = (uint)shared.Index.Count;
        }

        shared.Table.Save();

        workbookPart.Workbook.Save();
    }

    /// <summary>Excel のシート名制約（31 文字・禁止文字・重複）に合わせてサニタイズする。</summary>
    internal static string SanitizeSheetName(string? name, ISet<string> usedNames)
    {
        var cleaned = (name ?? string.Empty).Trim().Trim('\'');
        if (cleaned.Length == 0)
        {
            cleaned = "Sheet";
        }

        var builder = new StringBuilder(cleaned.Length);
        foreach (var ch in cleaned)
        {
            builder.Append(InvalidSheetNameChars.Contains(ch) ? '_' : ch);
        }

        var baseName = builder.ToString().Trim('\'');
        if (baseName.Length == 0)
        {
            baseName = "Sheet";
        }

        if (baseName.Length > MaxSheetNameLength)
        {
            baseName = baseName[..MaxSheetNameLength];
        }

        var candidate = baseName;
        var suffix = 2;
        while (!usedNames.Add(candidate))
        {
            var tag = " (" + suffix.ToString(CultureInfo.InvariantCulture) + ")";
            var keep = Math.Max(1, MaxSheetNameLength - tag.Length);
            candidate = baseName[..Math.Min(baseName.Length, keep)] + tag;
            suffix++;
        }

        return candidate;
    }

    private static Worksheet BuildWorksheet(ExportSheet sheet, SharedStringContext shared)
    {
        var worksheet = new Worksheet();

        var sheetView = new SheetView { WorkbookViewId = 0 };
        sheetView.AppendChild(new Pane
        {
            VerticalSplit = 1D,
            TopLeftCell = "A2",
            ActivePane = PaneValues.BottomLeft,
            State = PaneStateValues.FrozenSplit,
        });
        worksheet.AppendChild(new SheetViews(sheetView));

        if (sheet.Columns.Count > 0)
        {
            worksheet.AppendChild(BuildColumns(sheet));
        }

        var sheetData = new SheetData();
        uint rowIndex = 1;

        var headerRow = new Row { RowIndex = rowIndex };
        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            headerRow.AppendChild(CreateStringCell(shared, sheet.Columns[i], ColumnLetter(i) + rowIndex.ToString(CultureInfo.InvariantCulture)));
        }

        sheetData.AppendChild(headerRow);
        rowIndex++;

        foreach (var dataRow in sheet.Rows)
        {
            var row = new Row { RowIndex = rowIndex };
            var cellCount = Math.Min(sheet.Columns.Count, dataRow.Count);
            for (var i = 0; i < cellCount; i++)
            {
                var text = NormalizeCellText(dataRow[i]);
                if (text.Length == 0)
                {
                    continue;
                }

                row.AppendChild(CreateStringCell(shared, text, ColumnLetter(i) + rowIndex.ToString(CultureInfo.InvariantCulture)));
            }

            sheetData.AppendChild(row);
            rowIndex++;
        }

        worksheet.AppendChild(sheetData);

        if (sheet.Columns.Count > 0 && sheet.Rows.Count > 0)
        {
            worksheet.AppendChild(new AutoFilter
            {
                Reference = "A1:" + ColumnLetter(sheet.Columns.Count - 1) + (rowIndex - 1).ToString(CultureInfo.InvariantCulture),
            });
        }

        return worksheet;
    }

    private static Columns BuildColumns(ExportSheet sheet)
    {
        var widths = new double[sheet.Columns.Count];
        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            widths[i] = DisplayWidth(sheet.Columns[i]);
        }

        foreach (var row in sheet.Rows)
        {
            var cellCount = Math.Min(sheet.Columns.Count, row.Count);
            for (var i = 0; i < cellCount; i++)
            {
                var width = DisplayWidth(row[i]);
                if (width > widths[i])
                {
                    widths[i] = width;
                }
            }
        }

        var columns = new Columns();
        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            columns.AppendChild(new Column
            {
                Min = (uint)(i + 1),
                Max = (uint)(i + 1),
                Width = Math.Clamp(widths[i] + 2, MinColumnWidth, MaxColumnWidth),
                CustomWidth = true,
            });
        }

        return columns;
    }

    private static Cell CreateStringCell(SharedStringContext shared, string text, string reference)
    {
        if (!shared.Index.TryGetValue(text, out var index))
        {
            index = shared.Index.Count;
            shared.Index[text] = index;
            shared.Table.AppendChild(new SharedStringItem(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }

        shared.TotalReferences++;

        return new Cell
        {
            CellReference = reference,
            DataType = CellValues.SharedString,
            CellValue = new CellValue(index.ToString(CultureInfo.InvariantCulture)),
        };
    }

    /// <summary>ブック全体の共有文字列テーブルと参照数の管理。</summary>
    private sealed class SharedStringContext
    {
        public SharedStringTable Table { get; } = new();

        public Dictionary<string, int> Index { get; } = new(StringComparer.Ordinal);

        public int TotalReferences { get; set; }
    }

    private static Stylesheet BuildStylesheet()
    {
        var fonts = new Fonts(
            new Font(),
            new Font(new Bold(), new FontSize { Val = 11D }))
        {
            Count = 2,
        };

        var fills = new Fills(
            new Fill(),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFEDEDED" }) { PatternType = PatternValues.Solid }))
        {
            Count = 3,
        };

        var borders = new Borders(new Border()) { Count = 1 };

        var cellFormats = new CellFormats(
            new CellFormat(),
            new CellFormat
            {
                FontId = 1,
                FillId = 2,
                BorderId = 0,
                ApplyFont = true,
                ApplyFill = true,
            })
        {
            Count = 2,
        };

        return new Stylesheet(fonts, fills, borders, cellFormats);
    }

    /// <summary>セルに書けない文字（XML 制御文字など）を除去し、最大長に切り詰める。</summary>
    private static string NormalizeCellText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch is '\t' or '\n' or '\r')
            {
                builder.Append(ch);
                continue;
            }

            if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(ch).Append(value[i + 1]);
                i++;
                continue;
            }

            if (!char.IsSurrogate(ch) && XmlConvert.IsXmlChar(ch))
            {
                builder.Append(ch);
            }
        }

        var text = builder.ToString();
        return text.Length <= MaxCellTextLength ? text : text[..MaxCellTextLength];
    }

    /// <summary>表示幅（半角 = 1、全角 = 2）を概算する。</summary>
    private static double DisplayWidth(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        double width = 0;
        foreach (var ch in value)
        {
            if (ch is '\n' or '\r')
            {
                continue;
            }

            width += IsWideChar(ch) ? 2 : 1;
        }

        return width;
    }

    private static bool IsWideChar(char ch)
    {
        return ch >= 0x1100 &&
            (ch <= 0x115F ||
             (ch >= 0x2E80 && ch <= 0xA4CF) ||
             (ch >= 0xAC00 && ch <= 0xD7A3) ||
             (ch >= 0xF900 && ch <= 0xFAFF) ||
             (ch >= 0xFE30 && ch <= 0xFE4F) ||
             (ch >= 0xFF00 && ch <= 0xFF60) ||
             (ch >= 0xFFE0 && ch <= 0xFFE6));
    }

    /// <summary>0 始まりの列番号を Excel の列名（A, B, ..., Z, AA, ...）へ変換する。</summary>
    private static string ColumnLetter(int columnIndex)
    {
        var dividend = columnIndex + 1;
        var builder = new StringBuilder();
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            builder.Insert(0, (char)('A' + modulo));
            dividend = (dividend - modulo) / 26;
        }

        return builder.ToString();
    }
}
