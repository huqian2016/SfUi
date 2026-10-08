using System.Text;

namespace SfUi.Core;

/// <summary>エクスポートしたシートを xlsx（1 ブック複数シート） / CSV（1 シート 1 ファイル）として書き出す。</summary>
public static class OrgExportWriter
{
    public static List<string> Write(string outputDirectory, string baseName, IReadOnlyList<ExportSheet> sheets, bool excel, bool csv)
    {
        Directory.CreateDirectory(outputDirectory);
        var files = new List<string>();
        var safeBase = SanitizeFileName(string.IsNullOrWhiteSpace(baseName) ? "export" : baseName);

        if (excel)
        {
            var path = Path.Combine(outputDirectory, safeBase + ".xlsx");
            ExcelExporter.Write(path, sheets);
            files.Add(path);
        }

        if (csv)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in sheets)
            {
                var name = SanitizeFileName(sheet.Name);
                var candidate = name;
                var suffix = 2;
                while (!used.Add(candidate))
                {
                    candidate = $"{name} ({suffix++})";
                }

                var path = Path.Combine(outputDirectory, safeBase + "_" + candidate + ".csv");
                File.WriteAllText(path, sheet.ToCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                files.Add(path);
            }
        }

        return files;
    }

    /// <summary>ファイル名に使えない文字を置換する（シート名は既に Excel 制約でサニタイズ済みだが、CSV 用に二重の安全策）。</summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        var cleaned = builder.ToString().Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "export" : cleaned;
    }
}
