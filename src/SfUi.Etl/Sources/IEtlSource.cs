namespace SfUi.Etl.Sources;

/// <summary>
/// 入力ソース（ファイル / 組織など）の抽象。列名を先に確定し、行は逐次（ストリーミング可）で返す。
/// </summary>
public interface IEtlSource
{
    /// <summary>表示名（ファイル名など）。</summary>
    string Name { get; }

    /// <summary>列名（マッピング式から参照する名前）。</summary>
    IReadOnlyList<string> Columns { get; }

    /// <summary>データ行を順次返す（行の要素数は Columns 以下でも可。不足分は null 扱い）。</summary>
    IEnumerable<object?[]> ReadRows();
}

/// <summary>列名の正規化（空名 → Column<i>N</i>、重複 → 連番付与）。</summary>
public static class SourceColumnNames
{
    /// <summary>列名を一意化する（空 / 空白のみの名前は <c>Column{位置}</c> にする）。</summary>
    public static List<string> Normalize(IEnumerable<string?> rawNames)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var position = 0;

        foreach (var raw in rawNames)
        {
            position++;
            var name = string.IsNullOrWhiteSpace(raw) ? "Column" + position : raw.Trim();
            var candidate = name;
            var suffix = 2;
            while (!seen.Add(candidate))
            {
                candidate = name + "_" + suffix++;
            }

            result.Add(candidate);
        }

        return result;
    }
}
