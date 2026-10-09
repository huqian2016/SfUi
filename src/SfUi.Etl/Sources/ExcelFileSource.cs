using System.Text;
using ExcelDataReader;

namespace SfUi.Etl.Sources;

/// <summary>
/// Excel（.xlsx / .xls / .xlsb / .csv）ファイル ソース（ExcelDataReader）。
/// 先頭行をヘッダーとして扱い、日付セルは DateTime、数値セルは double のまま返す。
/// </summary>
public sealed class ExcelFileSource : IEtlSource
{
    static ExcelFileSource() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private readonly string _sheetName;
    private readonly bool _hasHeader;

    /// <param name="path">Excel ファイル パス。</param>
    /// <param name="sheetName">シート名（null = 先頭シート）。</param>
    /// <param name="hasHeader">先頭行をヘッダーとして扱うか（既定: true）。</param>
    public ExcelFileSource(string path, string? sheetName = null, bool hasHeader = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        _sheetName = sheetName ?? string.Empty;
        _hasHeader = hasHeader;

        using var reader = OpenReader();
        MoveToTargetSheet(reader);
        Columns = _hasHeader && reader.Read()
            ? SourceColumnNames.Normalize(
                Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i)?.ToString()))
            : SourceColumnNames.Normalize(Enumerable.Range(0, reader.FieldCount).Select(_ => (string?)null));
    }

    /// <summary>ファイル パス。</summary>
    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    public IReadOnlyList<string> Columns { get; }

    public IEnumerable<object?[]> ReadRows()
    {
        using var reader = OpenReader();
        MoveToTargetSheet(reader);

        if (_hasHeader)
        {
            reader.Read();  // ヘッダー行をスキップ
        }

        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = reader.GetValue(i);
            }

            yield return row;
        }
    }

    private IExcelDataReader OpenReader()
    {
        var stream = File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return ExcelReaderFactory.CreateReader(stream);
    }

    private void MoveToTargetSheet(IExcelDataReader reader)
    {
        do
        {
            if (_sheetName.Length == 0 ||
                string.Equals(reader.Name, _sheetName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        while (reader.NextResult());

        throw new InvalidOperationException($"シート '{_sheetName}' が見つかりません: {Path}");
    }
}
