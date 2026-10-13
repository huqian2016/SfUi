namespace SfUi.Core;

/// <summary>SOQL テキストの解析（Id 列の補完・対象オブジェクトの抽出）。</summary>
public static class SoqlQueryEditor
{
    /// <summary>
    /// SELECT 句に Id が無いとき、実行用クエリの SELECT 直後に「Id,」を補う。
    /// 集計・関数（COUNT() や FIELDS() など括弧を含む選択リスト）では何もしない。
    /// </summary>
    public static (string Query, bool Injected) EnsureIdSelected(string soql)
    {
        if (string.IsNullOrWhiteSpace(soql))
        {
            return (soql, false);
        }

        var select = FindKeyword(soql, "SELECT", 0);
        if (select < 0)
        {
            return (soql, false);
        }

        var selectEnd = select + "SELECT".Length;
        var from = FindKeyword(soql, "FROM", selectEnd);
        if (from < 0)
        {
            return (soql, false);
        }

        var fields = soql[selectEnd..from];
        if (fields.Contains('(') || ContainsIdToken(fields))
        {
            return (soql, false);
        }

        return (soql[..selectEnd] + " Id," + soql[selectEnd..], true);
    }

    /// <summary>トップレベルの FROM 直後のオブジェクト API 名を返す（取得できないときは null）。</summary>
    public static string? TryGetObjectName(string soql)
    {
        if (string.IsNullOrWhiteSpace(soql))
        {
            return null;
        }

        var from = FindKeyword(soql, "FROM", 0);
        if (from < 0)
        {
            return null;
        }

        var index = from + "FROM".Length;
        while (index < soql.Length && char.IsWhiteSpace(soql[index]))
        {
            index++;
        }

        var start = index;
        while (index < soql.Length && (char.IsAsciiLetterOrDigit(soql[index]) || soql[index] == '_'))
        {
            index++;
        }

        return index > start ? soql[start..index] : null;
    }

    /// <summary>選択リストに単独の Id が含まれるか（Account.Id や Id__c は対象外）。</summary>
    private static bool ContainsIdToken(string fields)
    {
        var index = 0;
        while (true)
        {
            var found = fields.IndexOf("id", index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            var before = found == 0 ? '\0' : fields[found - 1];
            var afterIndex = found + 2;
            var after = afterIndex >= fields.Length ? '\0' : fields[afterIndex];
            var boundaryBefore = before == '\0' || (!char.IsLetterOrDigit(before) && before != '_' && before != '.');
            var boundaryAfter = after == '\0' || (!char.IsLetterOrDigit(after) && after != '_');
            if (boundaryBefore && boundaryAfter)
            {
                return true;
            }

            index = found + 2;
        }
    }

    /// <summary>括弧の深さ 0 にあるキーワードを探す（文字列リテラル・識別子の一部は除外）。</summary>
    private static int FindKeyword(string text, string keyword, int start)
    {
        var depth = 0;
        for (var i = Math.Max(start, 0); i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\'')
            {
                // 文字列リテラルを読み飛ばす（'' と \' のエスケープに対応）
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '\\')
                    {
                        i++;
                    }
                    else if (text[i] == '\'')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    i++;
                }

                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }

                continue;
            }

            if (depth != 0 || !MatchesAt(text, i, keyword))
            {
                continue;
            }

            var before = i == 0 ? '\0' : text[i - 1];
            var afterIndex = i + keyword.Length;
            var after = afterIndex >= text.Length ? '\0' : text[afterIndex];
            if ((before == '\0' || (!char.IsLetterOrDigit(before) && before != '_'))
                && (after == '\0' || (!char.IsLetterOrDigit(after) && after != '_')))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool MatchesAt(string text, int index, string keyword) =>
        index + keyword.Length <= text.Length
        && string.Compare(text, index, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0;
}
