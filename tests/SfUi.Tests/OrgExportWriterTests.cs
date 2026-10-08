using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class OrgExportWriterTests
{
    [Fact]
    public void Write_creates_xlsx_and_csv_per_sheet()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sfui-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sheets = new List<ExportSheet>
            {
                new()
                {
                    Name = "オブジェクト定義",
                    Columns = ["API 名", "ラベル"],
                    Rows = new List<IReadOnlyList<string?>>
                    {
                        new List<string?> { "Account", "取引先" },
                    },
                },
                new()
                {
                    Name = "項目_Account",
                    Columns = ["API 名"],
                    Rows = new List<IReadOnlyList<string?>>
                    {
                        new List<string?> { "Name" },
                    },
                },
            };

            var files = OrgExportWriter.Write(directory, "定義書_test", sheets, excel: true, csv: true);

            Assert.Equal(3, files.Count);
            Assert.True(File.Exists(Path.Combine(directory, "定義書_test.xlsx")));

            var csvPath = files.Single(path => path.EndsWith("_項目_Account.csv", StringComparison.Ordinal));
            var bytes = File.ReadAllBytes(csvPath);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());

            var text = File.ReadAllText(csvPath);
            Assert.Equal("API 名\r\nName\r\n", text);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Write_excel_only_creates_single_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sfui-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sheets = new List<ExportSheet> { new() { Name = "A", Columns = ["x"] } };
            var files = OrgExportWriter.Write(directory, "base", sheets, excel: true, csv: false);

            Assert.Single(files);
            Assert.EndsWith(".xlsx", files[0], StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void SanitizeFileName_replaces_invalid_characters_and_trims()
    {
        Assert.Equal("a_b", OrgExportWriter.SanitizeFileName("a/b"));
        Assert.Equal("name", OrgExportWriter.SanitizeFileName("name."));
        Assert.Equal("export", OrgExportWriter.SanitizeFileName("   "));
        Assert.Equal("export", OrgExportWriter.SanitizeFileName("..."));
    }
}
