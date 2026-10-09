using SfUi.Etl.Connections;

namespace SfUi.Etl.Sources;

/// <summary>
/// CSV / TSV ファイル ソース（<see cref="CsvStreamReader"/> のストリーミング読み取りをそのまま利用）。
/// ヘッダーがない場合は <c>Column1..N</c> の名前を割り当てる。
/// </summary>
public sealed class CsvFileSource : IEtlSource
{
    private readonly CsvStreamOptions _options;

    public CsvFileSource(string path, CsvStreamOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        _options = options ?? new CsvStreamOptions();

        using var reader = new CsvStreamReader(path, _options);
        if (reader.Headers.Length > 0)
        {
            Columns = SourceColumnNames.Normalize(reader.Headers);
        }
        else
        {
            // ヘッダーなし: 先頭行から列数を推定
            var first = reader.ReadRows().FirstOrDefault();
            Columns = first is null
                ? Array.Empty<string>()
                : SourceColumnNames.Normalize(Enumerable.Range(0, first.Length).Select(_ => (string?)null));
        }
    }

    /// <summary>ファイル パス。</summary>
    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    public IReadOnlyList<string> Columns { get; }

    public IEnumerable<object?[]> ReadRows()
    {
        using var reader = new CsvStreamReader(Path, _options);
        foreach (var row in reader.ReadRows())
        {
            yield return row;
        }
    }
}
