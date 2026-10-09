using System.Text.Json;

namespace SfUi.Etl.Sources;

/// <summary>
/// JSON ファイル ソース。ルートは配列（または <paramref name="arrayProperty"/> で指定した配列プロパティ、
/// もしくは単一オブジェクト）。列は全レコードのプロパティ名の和集合（登場順）。
/// 値は文字列 / 数値（原文のまま）/ true / false / null、ネストした値は生 JSON テキスト。
/// </summary>
public sealed class JsonFileSource : IEtlSource
{
    private readonly List<Dictionary<string, object?>> _records = new();
    private readonly List<string> _columns = new();

    /// <param name="path">JSON ファイル パス。</param>
    /// <param name="arrayProperty">ルートがオブジェクトの場合に配列を持つプロパティ名（省略時は単一レコード扱い）。</param>
    public JsonFileSource(string path, string? arrayProperty = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;

        using var document = JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var root = document.RootElement;
        foreach (var item in JsonRecords.ResolveItems(root, arrayProperty))
        {
            JsonRecords.Add(_columns, _records, item);
        }
    }

    /// <summary>ファイル パス。</summary>
    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    public IReadOnlyList<string> Columns => _columns;

    public IEnumerable<object?[]> ReadRows()
    {
        foreach (var record in _records)
        {
            var row = new object?[_columns.Count];
            for (var i = 0; i < _columns.Count; i++)
            {
                record.TryGetValue(_columns[i], out row[i]);
            }

            yield return row;
        }
    }
}
