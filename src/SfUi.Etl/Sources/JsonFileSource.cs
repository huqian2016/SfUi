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
        var items = ResolveItems(root, arrayProperty);

        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("JSON の各レコードはオブジェクトである必要があります。");
            }

            var record = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in item.EnumerateObject())
            {
                if (!_columns.Contains(property.Name, StringComparer.Ordinal))
                {
                    _columns.Add(property.Name);
                }

                record[property.Name] = ToValue(property.Value);
            }

            _records.Add(record);
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

    private static List<JsonElement> ResolveItems(JsonElement root, string? arrayProperty)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                return root.EnumerateArray().ToList();

            case JsonValueKind.Object when !string.IsNullOrEmpty(arrayProperty):
                return root.TryGetProperty(arrayProperty, out var array) && array.ValueKind == JsonValueKind.Array
                    ? array.EnumerateArray().ToList()
                    : throw new InvalidOperationException($"JSON に配列プロパティ '{arrayProperty}' が見つかりません。");

            case JsonValueKind.Object:
                return new List<JsonElement> { root };

            default:
                throw new InvalidOperationException("JSON ルートは配列またはオブジェクトである必要があります。");
        }
    }

    private static object? ToValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.GetRawText(),
    };
}
