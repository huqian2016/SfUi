namespace SfUi.Core;

/// <summary>補完候補を表示する SOQL の句。</summary>
public enum SoqlClause
{
    None,
    Select,
    From,
    Where,
    GroupBy,
    OrderBy,
    Limit,
    Offset,
    Other,
}

/// <summary>カーソル位置から解析した補完コンテキスト。</summary>
public sealed record SoqlCompletionContext(
    SoqlClause Clause,
    string Prefix,
    int WordStart,
    IReadOnlyList<string> Path,
    IReadOnlyList<string> FromObjects,
    IReadOnlyDictionary<string, string> Aliases,
    bool InsideString)
{
    /// <summary>項目候補を出す句か。</summary>
    public bool IsFieldClause => Clause is SoqlClause.Select or SoqlClause.Where or SoqlClause.GroupBy or SoqlClause.OrderBy;

    /// <summary>SELECT 関数を候補に含める句か。</summary>
    public bool IncludeFunctions => Clause is SoqlClause.Select or SoqlClause.GroupBy;
}

/// <summary>補完候補 1 件。</summary>
public sealed record SoqlCompletionItem(string Text, string? Description = null, int CaretOffsetDelta = 0);

/// <summary>SOQL 補完のコンテキスト解析（純関数・テスト対象）。</summary>
public static class SoqlCompletionParser
{
    /// <summary>FROM 句のオブジェクト名リストを打ち切る語。</summary>
    private static readonly HashSet<string> ClauseBoundaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "GROUP", "ORDER", "LIMIT", "OFFSET", "WITH", "FOR", "TYPEOF", "BY", "HAVING",
    };

    private static readonly IReadOnlyDictionary<string, string> EmptyAliases = new Dictionary<string, string>();

    /// <summary>
    /// カーソル位置のテキストから補完コンテキストを解析する。
    /// 句はカーソルより前、FROM オブジェクトは全文（FROM はカーソルより後ろにあることが多い）から求める。
    /// </summary>
    public static SoqlCompletionContext Parse(string? text, int caret)
    {
        text ??= string.Empty;
        caret = Math.Clamp(caret, 0, text.Length);
        var head = text[..caret];

        if (IsInsideStringOrComment(head))
        {
            return new SoqlCompletionContext(SoqlClause.None, string.Empty, caret, Array.Empty<string>(), Array.Empty<string>(), EmptyAliases, true);
        }

        // カーソル直前の語（「Account.」のようなドット付きパスを含む）を切り出す
        var start = caret;
        while (start > 0 && (IsWordChar(text[start - 1]) || text[start - 1] == '.'))
        {
            start--;
        }

        var word = text[start..caret];
        var prefix = word;
        var path = new List<string>();
        var lastDot = word.LastIndexOf('.');
        if (lastDot >= 0)
        {
            prefix = word[(lastDot + 1)..];
            path.AddRange(word[..lastDot].Split('.', StringSplitOptions.RemoveEmptyEntries));
        }

        var clause = DetectClause(head[..start]);
        var (objects, aliases) = ParseFromList(text);
        return new SoqlCompletionContext(clause, prefix, start, path, objects, aliases, false);
    }

    /// <summary>カーソルより前のテキストから現在の句を判定する。</summary>
    public static SoqlClause DetectClause(string headText)
    {
        var clause = SoqlClause.None;
        foreach (var token in Tokenize(StripLiterals(headText)))
        {
            if (!token.IsWord)
            {
                continue;
            }

            switch (token.Word.ToUpperInvariant())
            {
                case "SELECT":
                    clause = SoqlClause.Select;
                    break;
                case "FROM":
                    clause = SoqlClause.From;
                    break;
                case "WHERE":
                    clause = SoqlClause.Where;
                    break;
                case "GROUP":
                    clause = SoqlClause.GroupBy;
                    break;
                case "ORDER":
                    clause = SoqlClause.OrderBy;
                    break;
                case "LIMIT":
                    clause = SoqlClause.Limit;
                    break;
                case "OFFSET":
                    clause = SoqlClause.Offset;
                    break;
                case "WITH":
                case "FOR":
                case "TYPEOF":
                    clause = SoqlClause.Other;
                    break;
            }
        }

        return clause;
    }

    /// <summary>全文から最初の FROM 句のオブジェクト名とエイリアスを抽出する。</summary>
    public static (IReadOnlyList<string> Objects, IReadOnlyDictionary<string, string> Aliases) ParseFromList(string text)
    {
        var tokens = Tokenize(StripLiterals(text));
        var objects = new List<string>();
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < tokens.Count; i++)
        {
            if (!tokens[i].IsWord || !tokens[i].Word.Equals("FROM", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var j = i + 1;
            while (j < tokens.Count)
            {
                while (j < tokens.Count && !tokens[j].IsWord)
                {
                    j++;   // カンマ等を飛ばす
                }

                if (j >= tokens.Count)
                {
                    break;
                }

                var name = tokens[j].Word;
                if (ClauseBoundaries.Contains(name))
                {
                    break;
                }

                objects.Add(name);
                j++;

                // 次の語がキーワードでなければエイリアス
                if (j < tokens.Count && tokens[j].IsWord && !ClauseBoundaries.Contains(tokens[j].Word))
                {
                    aliases[tokens[j].Word] = name;
                    j++;
                }

                // カンマで続く場合は複数オブジェクト（FROM Contact c, Account a）
                if (j < tokens.Count && tokens[j].IsComma)
                {
                    j++;
                    continue;
                }

                break;
            }

            break;   // 最初の FROM のみ対象（サブクエリは非対応）
        }

        return (objects, aliases);
    }

    /// <summary>カーソルが文字列リテラルまたは行コメント（--）の中にあるか。</summary>
    public static bool IsInsideStringOrComment(string head)
    {
        var insideString = false;
        for (var i = 0; i < head.Length; i++)
        {
            var c = head[i];
            if (insideString)
            {
                if (c == '\\')
                {
                    i++;   // エスケープ（\' など）
                }
                else if (c == '\'')
                {
                    insideString = false;
                }

                continue;
            }

            if (c == '\'')
            {
                insideString = true;
                continue;
            }

            if (c == '-' && i + 1 < head.Length && head[i + 1] == '-')
            {
                var newline = head.IndexOf('\n', i + 2);
                if (newline < 0)
                {
                    return true;   // 行末までコメント = カーソルはコメント内
                }

                i = newline;
            }
        }

        return insideString;
    }

    /// <summary>文字列リテラルと行コメントを空白に置き換える（長さは保持 = 位置がずれない）。</summary>
    public static string StripLiterals(string text)
    {
        var chars = text.ToCharArray();
        var insideString = false;
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (insideString)
            {
                if (c == '\\')
                {
                    chars[i] = ' ';
                    if (i + 1 < chars.Length)
                    {
                        chars[i + 1] = ' ';
                        i++;
                    }

                    continue;
                }

                if (c == '\'')
                {
                    insideString = false;
                }

                chars[i] = ' ';
                continue;
            }

            if (c == '\'')
            {
                insideString = true;
                chars[i] = ' ';
                continue;
            }

            if (c == '-' && i + 1 < chars.Length && chars[i + 1] == '-')
            {
                while (i < chars.Length && chars[i] != '\n')
                {
                    chars[i] = ' ';
                    i++;
                }

                i--;   // 外側の for で 1 進む
            }
        }

        return new string(chars);
    }

    private static List<SoqlToken> Tokenize(string text)
    {
        var tokens = new List<SoqlToken>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (IsWordChar(c))
            {
                var start = i;
                while (i < text.Length && IsWordChar(text[i]))
                {
                    i++;
                }

                tokens.Add(new SoqlToken(text[start..i], IsWord: true, IsComma: false));
            }
            else
            {
                if (c == ',')
                {
                    tokens.Add(new SoqlToken(",", IsWord: false, IsComma: true));
                }

                i++;
            }
        }

        return tokens;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private readonly record struct SoqlToken(string Word, bool IsWord, bool IsComma);
}

/// <summary>SOQL の補完候補を組み立てる（純関数・テスト対象）。</summary>
public static class SoqlCompletionEngine
{
    /// <summary>候補の上限（巨大な項目一覧で UI が重くならないように）。</summary>
    public const int MaxItems = 400;

    /// <summary>SELECT / GROUP BY で候補に出す関数（呼び出し形）。</summary>
    public static readonly IReadOnlyList<string> FunctionCalls = new[]
    {
        "COUNT()", "COUNT(Id)", "COUNT_DISTINCT(Id)", "SUM()", "AVG()", "MIN()", "MAX()",
        "CALENDAR_MONTH()", "CALENDAR_QUARTER()", "CALENDAR_YEAR()",
        "FISCAL_MONTH()", "FISCAL_QUARTER()", "FISCAL_YEAR()", "FISCAL_WEEK()",
        "DAY_IN_MONTH()", "DAY_IN_WEEK()", "DAY_IN_YEAR()", "DAY_ONLY()",
        "HOUR_IN_DAY()", "WEEK_IN_MONTH()", "WEEK_IN_YEAR()",
        "FORMAT()", "CONVERTCURRENCY()", "CONVERTTIMEZONE()", "TO_LABEL()", "GROUPING()",
    };

    /// <summary>FROM 句のオブジェクト名候補（クエリ可能なもののみ・大文字小文字を無視したプレフィックス一致）。</summary>
    public static IReadOnlyList<SoqlCompletionItem> ObjectItems(IReadOnlyList<DataIoObject> objects, string prefix)
    {
        return objects
            .Where(o => o.Queryable && StartsWith(o.Name, prefix))
            .OrderBy(o => o.Name, StringComparer.Ordinal)
            .Take(MaxItems)
            .Select(o => new SoqlCompletionItem(
                o.Name,
                string.Equals(o.Name, o.Label, StringComparison.Ordinal) ? null : o.Label))
            .ToList();
    }

    /// <summary>
    /// 項目候補。参照項目は項目名（AccountId）と参照名（Account.）の 2 候補を返し、
    /// SELECT / GROUP BY では関数も候補に含める。
    /// </summary>
    public static IReadOnlyList<SoqlCompletionItem> FieldItems(DataIoObjectDescribe describe, string prefix, bool includeFunctions)
    {
        var items = new List<SoqlCompletionItem>();

        if (includeFunctions)
        {
            foreach (var call in FunctionCalls)
            {
                if (StartsWith(call, prefix))
                {
                    items.Add(new SoqlCompletionItem(call, null, call.EndsWith(')') ? -1 : 0));
                }
            }
        }

        foreach (var field in describe.Fields.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            if (StartsWith(field.Name, prefix))
            {
                items.Add(new SoqlCompletionItem(field.Name, FieldDescription(field)));
            }

            if (!string.IsNullOrEmpty(field.RelationshipName) && StartsWith(field.RelationshipName, prefix))
            {
                items.Add(new SoqlCompletionItem(
                    field.RelationshipName + ".",
                    UiText.T("Soql_CompletionRelationFmt", field.RelationshipName)));
            }
        }

        return items.Take(MaxItems).ToList();
    }

    /// <summary>パス 1 区間を関係名（relationshipName）として解決する（見つからなければ null）。</summary>
    public static string? ResolveRelationship(DataIoObjectDescribe describe, string segment)
    {
        foreach (var field in describe.Fields)
        {
            if (!string.IsNullOrEmpty(field.RelationshipName)
                && string.Equals(field.RelationshipName, segment, StringComparison.OrdinalIgnoreCase))
            {
                return field.ReferenceTo.FirstOrDefault();
            }
        }

        return null;
    }

    /// <summary>
    /// 項目候補の対象オブジェクトを解決する（エイリアス / 別オブジェクト / 参照関係の連鎖）。
    /// </summary>
    public static async Task<string?> ResolveTargetAsync(
        SObjectDescribeService describes, string org, SoqlCompletionContext context, CancellationToken cancellationToken = default)
    {
        string? target = context.FromObjects.FirstOrDefault();

        for (var i = 0; i < context.Path.Count; i++)
        {
            var segment = context.Path[i];

            if (i == 0)
            {
                if (context.Aliases.TryGetValue(segment, out var aliased))
                {
                    target = aliased;
                    continue;
                }

                var fromMatch = context.FromObjects.FirstOrDefault(o => string.Equals(o, segment, StringComparison.OrdinalIgnoreCase));
                if (fromMatch is not null)
                {
                    target = fromMatch;
                    continue;
                }
            }

            if (target is null)
            {
                return null;
            }

            var describe = await describes.DescribeAsync(org, target, cancellationToken: cancellationToken).ConfigureAwait(false);
            var relationship = ResolveRelationship(describe, segment);
            if (relationship is null)
            {
                return null;
            }

            target = relationship;
        }

        return target;
    }

    private static string FieldDescription(DataIoField field)
    {
        var type = string.IsNullOrEmpty(field.Type) ? "string" : field.Type;
        return string.IsNullOrWhiteSpace(field.Label) || string.Equals(field.Label, field.Name, StringComparison.Ordinal)
            ? type
            : $"{field.Label} ({type})";
    }

    private static bool StartsWith(string value, string prefix) =>
        prefix.Length == 0 || value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
