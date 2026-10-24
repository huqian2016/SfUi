using System.Text;

namespace SfUi.Core;

/// <summary>
/// レポート 1 件を指定された形式すべてへ書き出す。
/// - CSV: 表ごとに 1 ファイル
/// - Markdown: AI 向け（YAML フロント マター + 表）
/// - HTML: 絞り込み・ソート付きの単一ファイル
/// - Excel: 概要シート + 表ごとのシート（1 ブック）
/// - PDF: HTML をヘッドレス ブラウザーで印刷
/// </summary>
public static class ReportExportService
{
    /// <summary>レポートを書き出す（作成したファイルのパスを返す）。</summary>
    public static async Task<ReportExportResult> ExportAsync(
        ReportDocument document,
        ReportExportOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.Formats == ReportFormats.None)
        {
            return new ReportExportResult { Files = Array.Empty<string>() };
        }

        var directory = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Environment.CurrentDirectory
            : options.OutputDirectory;
        Directory.CreateDirectory(directory);

        var baseName = SanitizeFileName(string.IsNullOrWhiteSpace(options.BaseName)
            ? "report-" + document.GeneratedAt.ToString("yyyyMMdd-HHmmss")
            : options.BaseName);

        var prepared = Prepare(document, options);
        var files = new List<string>();

        if (options.Formats.HasFlag(ReportFormats.Csv))
        {
            files.AddRange(WriteCsv(prepared, directory, baseName));
        }

        if (options.Formats.HasFlag(ReportFormats.Markdown))
        {
            var path = Path.Combine(directory, baseName + ".md");
            await File.WriteAllTextAsync(path, ReportMarkdownWriter.Write(prepared), new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            files.Add(path);
        }

        string? html = null;
        if (options.Formats.HasFlag(ReportFormats.Html) || options.Formats.HasFlag(ReportFormats.Pdf))
        {
            html = ReportHtmlWriter.Write(prepared);
        }

        if (options.Formats.HasFlag(ReportFormats.Html) && html is not null)
        {
            var path = Path.Combine(directory, baseName + ".html");
            await File.WriteAllTextAsync(path, html, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            files.Add(path);
        }

        if (options.Formats.HasFlag(ReportFormats.Excel))
        {
            var path = Path.Combine(directory, baseName + ".xlsx");
            ExcelExporter.Write(path, BuildSheets(prepared));
            files.Add(path);
        }

        if (options.Formats.HasFlag(ReportFormats.Pdf) && html is not null)
        {
            var path = Path.Combine(directory, baseName + ".pdf");
            await ReportPdfWriter.WriteAsync(html, path, cancellationToken).ConfigureAwait(false);
            files.Add(path);
        }

        return new ReportExportResult { Files = files };
    }

    /// <summary>行数制限と分析セクションの付与を行う。</summary>
    private static ReportDocument Prepare(ReportDocument document, ReportExportOptions options)
    {
        var limited = options.MaxRows > 0 ? document.WithRowLimit(options.MaxRows) : document;
        if (!options.IncludeAnalysis)
        {
            return limited;
        }

        foreach (var table in limited.Tables.ToList())
        {
            limited.Sections.Add(ReportAnalysis.Analyze(table));
        }

        return limited;
    }

    private static IEnumerable<string> WriteCsv(ReportDocument document, string directory, string baseName)
    {
        var tables = document.Tables.ToList();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            var suffix = tables.Count == 1 ? string.Empty : "-" + SanitizeFileName(table.Name);
            var name = baseName + suffix;
            var unique = name;
            var counter = 2;
            while (!used.Add(unique))
            {
                unique = name + "-" + counter++;
            }

            var sheet = new ExportSheet
            {
                Name = table.Name,
                Columns = table.Columns,
                Rows = table.Rows,
            };
            var path = Path.Combine(directory, unique + ".csv");
            File.WriteAllText(path, sheet.ToCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            yield return path;
        }
    }

    private static IReadOnlyList<ExportSheet> BuildSheets(ReportDocument document)
    {
        var sheets = new List<ExportSheet>();

        // 概要シート（タイトル・メタ情報・分析の箇条書き）
        var summaryColumns = new List<string> { UiText.T("Report_MetaItem"), UiText.T("Report_MetaValue") };
        var summaryRows = new List<IReadOnlyList<string?>>
        {
            Row(UiText.T("Report_MetaTitle"), document.Title),
            Row(UiText.T("Report_MetaGenerated"), document.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss")),
        };
        if (!string.IsNullOrWhiteSpace(document.Org))
        {
            summaryRows.Add(Row(UiText.T("Report_MetaOrg"), document.Org));
        }

        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            summaryRows.Add(Row(UiText.T("Report_MetaSubtitle"), document.Subtitle));
        }

        foreach (var section in document.Sections.Where(section => section.Bullets is { Count: > 0 }))
        {
            foreach (var bullet in section.Bullets!)
            {
                summaryRows.Add(Row(section.Heading, bullet));
            }
        }

        sheets.Add(new ExportSheet { Name = UiText.T("Report_SheetSummary"), Columns = summaryColumns, Rows = summaryRows });

        foreach (var section in document.Sections.Where(section => section.Table is not null))
        {
            sheets.Add(new ExportSheet
            {
                Name = section.Table!.Name,
                Columns = section.Table.Columns,
                Rows = section.Table.Rows,
            });
        }

        return sheets;
    }

    private static IReadOnlyList<string?> Row(string? left, string? right) => new[] { left, right };

    internal static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? "report" : result;
    }
}
