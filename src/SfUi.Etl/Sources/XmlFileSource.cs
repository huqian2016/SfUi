using System.Xml.Linq;

namespace SfUi.Etl.Sources;

/// <summary>
/// XML ファイル ソース。行要素（既定: ルートの最初の子要素名、またはルート直下で最初に見つかった同名要素）の
/// 直下の子要素を列として扱う（値は要素のテキスト）。列は全レコードの和集合（登場順）。
/// </summary>
public sealed class XmlFileSource : IEtlSource
{
    private readonly List<XElement> _records = new();
    private readonly List<string> _columns = new();

    /// <param name="path">XML ファイル パス。</param>
    /// <param name="rowElement">行要素名（null = 自動判定: ルートの最初の子要素）。名前空間なしのローカル名で比較する。</param>
    public XmlFileSource(string path, string? rowElement = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;

        var document = XDocument.Load(path, LoadOptions.None);
        var root = document.Root ?? throw new InvalidOperationException("XML にルート要素がありません。");

        RowElementName = rowElement
            ?? root.Elements().FirstOrDefault()?.Name.LocalName
            ?? throw new InvalidOperationException("行要素を特定できません。rowElement を指定してください。");

        _records = root.Elements().Where(e => e.Name.LocalName == RowElementName).ToList();
        if (_records.Count == 0)
        {
            _records = root.Descendants().Where(e => e.Name.LocalName == RowElementName).ToList();
        }

        foreach (var record in _records)
        {
            foreach (var child in record.Elements())
            {
                if (!_columns.Contains(child.Name.LocalName, StringComparer.Ordinal))
                {
                    _columns.Add(child.Name.LocalName);
                }
            }
        }
    }

    /// <summary>ファイル パス。</summary>
    public string Path { get; }

    /// <summary>行要素名。</summary>
    public string RowElementName { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    public IReadOnlyList<string> Columns => _columns;

    public IEnumerable<object?[]> ReadRows()
    {
        foreach (var record in _records)
        {
            var row = new object?[_columns.Count];
            for (var i = 0; i < _columns.Count; i++)
            {
                var name = _columns[i];
                row[i] = record.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
            }

            yield return row;
        }
    }
}
