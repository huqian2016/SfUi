using System.Text.RegularExpressions;

namespace SfUi.Core;

/// <summary>Apex 補完候補の種類。</summary>
public enum ApexCompletionKind
{
    /// <summary>候補なし。</summary>
    None,

    /// <summary>単語入力（構文・スニペット・型・静的クラス）。</summary>
    Snippets,

    /// <summary>静的クラス・変数のメンバー（例: System. / a.Owner.）。</summary>
    Members,

    /// <summary>インライン SOQL（[SELECT ...]）の内部。SOQL 候補を流用する。</summary>
    Soql,

    /// <summary>sObject 名候補（new / List< / Map<... の直後）。</summary>
    Objects,

    /// <summary>DML 対象の変数候補（insert / update / … の直後）。</summary>
    Variables,
}

/// <summary>カーソル位置から解析した Apex 補完コンテキスト。</summary>
public sealed record ApexCompletionContext(
    ApexCompletionKind Kind,
    string Prefix,
    int SegmentStart,
    string? Root = null,
    IReadOnlyList<string>? Path = null,
    SoqlCompletionContext? Soql = null);

/// <summary>匿名Apex 補完のコンテキスト解析（純関数・テスト対象）。</summary>
public static class ApexCompletionParser
{
    private const string SoqlStartKeyword = "SELECT";

    /// <summary>
    /// カーソル位置のテキストから補完コンテキストを解析する。
    /// インライン SOQL（[SELECT ...]）→ 静的クラスのメンバー（X.）→ 単語（スニペット）の順に判定する。
    /// </summary>
    public static ApexCompletionContext Parse(string? text, int caret)
    {
        text ??= string.Empty;
        caret = Math.Clamp(caret, 0, text.Length);
        var head = text[..caret];

        if (IsInsideStringOrComment(head))
        {
            return new ApexCompletionContext(ApexCompletionKind.None, string.Empty, caret);
        }

        // インライン SOQL（[SELECT ...]）の内部か
        var stripped = StripLiterals(text);
        var open = FindOpenBracket(stripped, caret);
        if (open >= 0 && LooksLikeSoql(stripped, open, caret))
        {
            var innerCaret = caret - open - 1;
            var inner = SoqlCompletionParser.Parse(text[(open + 1)..], innerCaret);
            return new ApexCompletionContext(
                ApexCompletionKind.Soql, inner.Prefix, caret - inner.Prefix.Length, Soql: inner);
        }

        // カーソル直前の語（ドット付きパスを含む）を切り出す
        var start = caret;
        while (start > 0 && (IsWordChar(text[start - 1]) || text[start - 1] == '.'))
        {
            start--;
        }

        var word = text[start..caret];
        var lastDot = word.LastIndexOf('.');
        if (lastDot >= 0)
        {
            var parent = word[..lastDot];
            var prefix = word[(lastDot + 1)..];
            var segments = parent.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return new ApexCompletionContext(ApexCompletionKind.None, prefix, caret - prefix.Length);
            }

            var path = segments.Length > 1 ? segments[1..] : null;
            return new ApexCompletionContext(
                ApexCompletionKind.Members, prefix, caret - prefix.Length, Root: segments[0], Path: path);
        }

        // キーワード・コレクション型の直後は語が空でも候補を出す
        var before = text[..start];
        if (EndsWithKeyword(before, DmlKeywords))
        {
            return new ApexCompletionContext(ApexCompletionKind.Variables, word, start);
        }

        if (EndsWithKeyword(before, "new"))
        {
            return new ApexCompletionContext(ApexCompletionKind.Objects, word, start);
        }

        if (IsInsideGenericAngle(text, start))
        {
            return new ApexCompletionContext(ApexCompletionKind.Objects, word, start);
        }

        return word.Length == 0
            ? new ApexCompletionContext(ApexCompletionKind.None, string.Empty, caret)
            : new ApexCompletionContext(ApexCompletionKind.Snippets, word, start);
    }

    /// <summary>カーソルが文字列リテラルまたはコメント（// と /* */）の中にあるか。</summary>
    public static bool IsInsideStringOrComment(string head)
    {
        var i = 0;
        while (i < head.Length)
        {
            var c = head[i];
            if (c == '\'')
            {
                i++;
                while (i < head.Length && head[i] != '\'')
                {
                    if (head[i] == '\\')
                    {
                        i++;
                    }

                    i++;
                }

                if (i >= head.Length)
                {
                    return true;   // 閉じていない = 文字列内
                }

                i++;   // 閉じクォート
                continue;
            }

            if (c == '/' && i + 1 < head.Length)
            {
                if (head[i + 1] == '/')
                {
                    var newline = head.IndexOf('\n', i + 2);
                    if (newline < 0)
                    {
                        return true;   // 行末までコメント = カーソルはコメント内
                    }

                    i = newline + 1;
                    continue;
                }

                if (head[i + 1] == '*')
                {
                    var end = head.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        return true;   // 閉じていないブロックコメント内
                    }

                    i = end + 2;
                    continue;
                }
            }

            i++;
        }

        return false;
    }

    /// <summary>文字列リテラルとコメントを空白に置き換える（長さは保持 = 位置がずれない）。</summary>
    public static string StripLiterals(string text)
    {
        var chars = text.ToCharArray();
        var i = 0;
        while (i < chars.Length)
        {
            var c = chars[i];
            if (c == '\'')
            {
                chars[i] = ' ';
                i++;
                while (i < chars.Length && chars[i] != '\'')
                {
                    if (chars[i] == '\\' && i + 1 < chars.Length)
                    {
                        chars[i] = ' ';
                        i++;
                    }

                    chars[i] = ' ';
                    i++;
                }

                if (i < chars.Length)
                {
                    chars[i] = ' ';   // 閉じクォート
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
            {
                while (i < chars.Length && chars[i] != '\n')
                {
                    chars[i] = ' ';
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
            {
                var closed = false;
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i += 2;
                while (i < chars.Length)
                {
                    if (chars[i] == '*' && i + 1 < chars.Length && chars[i + 1] == '/')
                    {
                        chars[i] = ' ';
                        chars[i + 1] = ' ';
                        i += 2;
                        closed = true;
                        break;
                    }

                    chars[i] = ' ';
                    i++;
                }

                if (!closed)
                {
                    break;   // 閉じていないコメント = 以降すべて空白
                }

                continue;
            }

            i++;
        }

        return new string(chars);
    }

    /// <summary>カーソルより前で閉じられていない '[' の位置を返す（なければ -1）。</summary>
    private static int FindOpenBracket(string stripped, int caret)
    {
        for (var i = caret - 1; i >= 0; i--)
        {
            var c = stripped[i];
            if (c == ']')
            {
                return -1;
            }

            if (c == '[')
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>'[' の直後（空白を除く）が SELECT で始まるか。</summary>
    private static bool LooksLikeSoql(string stripped, int open, int caret)
    {
        var inner = stripped[(open + 1)..caret].TrimStart();
        if (!inner.StartsWith(SoqlStartKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return inner.Length == SoqlStartKeyword.Length || !IsWordChar(inner[SoqlStartKeyword.Length]);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static readonly string[] DmlKeywords = { "insert", "update", "upsert", "delete", "undelete" };

    /// <summary>カーソル前テキストが指定キーワード（単語境界）で終わるか。</summary>
    private static bool EndsWithKeyword(string beforeText, string keyword)
    {
        var i = beforeText.Length - 1;
        while (i >= 0 && char.IsWhiteSpace(beforeText[i]))
        {
            i--;
        }

        var end = i + 1;
        var start = end - keyword.Length;
        if (start < 0)
        {
            return false;
        }

        if (string.Compare(beforeText, start, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        var prev = start - 1;
        return prev < 0 || (!IsWordChar(beforeText[prev]) && beforeText[prev] != '.');
    }

    private static bool EndsWithKeyword(string beforeText, string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (EndsWithKeyword(beforeText, keyword))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>カーソル位置が List&lt;…&gt; / Map&lt;…&gt; などの山括弧の中か。</summary>
    private static bool IsInsideGenericAngle(string text, int position)
    {
        var i = position - 1;
        while (i >= 0 && text[i] == ' ')
        {
            i--;
        }

        if (i < 0)
        {
            return false;
        }

        if (text[i] == '<')
        {
            return true;
        }

        if (text[i] != ',')
        {
            return false;
        }

        // Map<Id, | のような 2 つ目の引数
        var j = i - 1;
        while (j >= 0 && text[j] != '<' && text[j] != ';' && text[j] != '=' && text[j] != '\n' && text[j] != '(' && text[j] != '{')
        {
            j--;
        }

        return j >= 0 && text[j] == '<';
    }

    /// <summary>
    /// コード中の変数宣言（Type name / Type&lt;…&gt; name / for-each / メソッド引数）を走査し、
    /// 変数名 → 宣言された型（例: Account, List&lt;Contact&gt;）のマップを返す（純関数・テスト対象）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ScanDeclarations(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in DeclarationPattern.Matches(StripLiterals(text)))
        {
            var type = match.Groups["type"].Value.Trim();
            var name = match.Groups["name"].Value.Trim();
            if (NonTypeKeywords.Contains(type) || NonTypeKeywords.Contains(name) || type.Length == 0)
            {
                continue;
            }

            result[name] = type;   // 後勝ち = カーソルに近い宣言を優先
        }

        return result;
    }

    private static readonly Regex DeclarationPattern = new(
        @"(?<type>[A-Za-z_][A-Za-z0-9_\.]*(?:\s*<[^<>;=]*>)?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?=[=;,:)])",
        RegexOptions.Compiled);

    private static readonly HashSet<string> NonTypeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "for", "while", "do", "return", "new", "throw", "try", "catch", "finally", "switch", "when",
        "insert", "update", "upsert", "delete", "undelete", "merge", "break", "continue", "instanceof",
        "static", "final", "public", "private", "protected", "global", "abstract", "virtual", "override", "transient",
        "this", "super", "class", "interface", "enum", "extends", "implements",
    };
}

/// <summary>匿名Apex の補完候補を組み立てる（純関数・テスト対象）。</summary>
public static class ApexCompletionEngine
{
    /// <summary>候補の上限。</summary>
    public const int MaxItems = 200;

    private static readonly IReadOnlyList<(string Text, string Description, int Cursor)> Templates = new[]
    {
        T("System.debug(|);", "void"),
        T("if (|) {\n    \n}", "if"),
        T("for (Integer i = 0; i < records.size(); i++) {\n    |\n}", "for"),
        T("for (SObject record : records) {\n    |\n}", "for-each"),
        T("while (|) {\n    \n}", "while"),
        T("try {\n    |\n} catch (Exception e) {\n    System.debug(e);\n}", "try-catch"),
        T("switch on | {\n    when 'value' {\n        \n    }\n}", "switch"),
        T("System.assert(|);", "void"),
    };

    private static readonly IReadOnlyList<(string Text, string Description, int Cursor)> Types = new[]
    {
        T("String|", "Type"),
        T("Integer|", "Type"),
        T("Long|", "Type"),
        T("Double|", "Type"),
        T("Decimal|", "Type"),
        T("Boolean|", "Type"),
        T("Date|", "Type"),
        T("Datetime|", "Type"),
        T("Time|", "Type"),
        T("Id|", "Type"),
        T("Object|", "Type"),
        T("SObject|", "Type"),
        T("Blob|", "Type"),
        T("Exception|", "Type"),
        T("DmlException|", "Type"),
        T("QueryException|", "Type"),
        T("NullPointerException|", "Type"),
        T("List<|>", "List"),
        T("Set<|>", "Set"),
        T("Map<|>", "Map"),
    };

    private static readonly IReadOnlyList<(string Text, string Description, int Cursor)> KeywordsAndClasses = new[]
    {
        T("else|", "Keyword"),
        T("return|", "Keyword"),
        T("break|", "Keyword"),
        T("continue|", "Keyword"),
        T("throw|", "Keyword"),
        T("insert|", "DML"),
        T("update|", "DML"),
        T("upsert|", "DML"),
        T("delete|", "DML"),
        T("undelete|", "DML"),
        T("merge|", "DML"),
        T("new|", "Keyword"),
        T("true|", "Keyword"),
        T("false|", "Keyword"),
        T("null|", "Keyword"),
        T("static|", "Keyword"),
        T("final|", "Keyword"),
        T("public|", "Keyword"),
        T("private|", "Keyword"),
        T("protected|", "Keyword"),
        T("global|", "Keyword"),
        T("class|", "Keyword"),
        T("interface|", "Keyword"),
        T("enum|", "Keyword"),
        T("void|", "Keyword"),
        T("extends|", "Keyword"),
        T("implements|", "Keyword"),
        T("instanceof|", "Keyword"),
        T("System|", "Static class"),
        T("Database|", "Static class"),
        T("Test|", "Static class"),
        T("Limits|", "Static class"),
        T("JSON|", "Static class"),
        T("Math|", "Static class"),
        T("Trigger|", "Context class"),
        T("ApexPages|", "Static class"),
        T("Messaging|", "Static class"),
        T("Schema|", "Static class"),
        T("Http|", "Type"),
        T("HttpRequest|", "Type"),
        T("HttpResponse|", "Type"),
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Text, string Description)>> Members =
        new Dictionary<string, IReadOnlyList<(string Text, string Description)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["System"] = new[]
            {
                M("debug()", "void"),
                M("assert()", "void"),
                M("assertEquals()", "void"),
                M("assertNotEquals()", "void"),
                M("assertNull()", "void"),
                M("assertNotNull()", "void"),
                M("now()", "Datetime"),
                M("today()", "Date"),
                M("currentTimeMillis()", "Long"),
                M("isBatch()", "Boolean"),
                M("isFuture()", "Boolean"),
                M("isQueueable()", "Boolean"),
                M("isScheduled()", "Boolean"),
                M("enqueueJob()", "Id"),
                M("abortJob()", "void"),
                M("schedule()", "String"),
            },
            ["Database"] = new[]
            {
                M("insert()", "Database.SaveResult"),
                M("update()", "Database.SaveResult"),
                M("upsert()", "Database.UpsertResult"),
                M("delete()", "Database.DeleteResult"),
                M("undelete()", "Database.UndeleteResult"),
                M("merge()", "void"),
                M("query()", "List<SObject>"),
                M("countQuery()", "Integer"),
                M("getQueryLocator()", "Database.QueryLocator"),
                M("executeBatch()", "Id"),
                M("setSavepoint()", "Savepoint"),
                M("rollback()", "void"),
                M("releaseSavepoint()", "void"),
            },
            ["Test"] = new[]
            {
                M("startTest()", "void"),
                M("stopTest()", "void"),
                M("isRunningTest()", "Boolean"),
                M("setCreatedDate()", "void"),
                M("setFixedSearchResults()", "void"),
                M("setCurrentPage()", "void"),
                M("getStandardPricebookId()", "Id"),
            },
            ["Limits"] = new[]
            {
                M("getQueries()", "Integer"),
                M("getQueryRows()", "Integer"),
                M("getDmlStatements()", "Integer"),
                M("getDmlRows()", "Integer"),
                M("getCpuTime()", "Long"),
                M("getHeapSize()", "Integer"),
                M("getCallouts()", "Integer"),
                M("getEmailInvocations()", "Integer"),
                M("getFutureCalls()", "Integer"),
                M("getAggregateQueries()", "Integer"),
                M("getLimitQueries()", "Integer"),
                M("getLimitQueryRows()", "Integer"),
                M("getLimitDmlStatements()", "Integer"),
                M("getLimitCpuTime()", "Long"),
                M("getLimitHeapSize()", "Integer"),
            },
            ["JSON"] = new[]
            {
                M("serialize()", "String"),
                M("serializePretty()", "String"),
                M("deserialize()", "Object"),
                M("deserializeUntyped()", "Object"),
                M("createParser()", "JSONParser"),
                M("createGenerator()", "JSONGenerator"),
            },
            ["Date"] = new[]
            {
                M("today()", "Date"),
                M("newInstance()", "Date"),
                M("valueOf()", "Date"),
                M("daysInMonth()", "Integer"),
                M("isLeapYear()", "Boolean"),
            },
            ["Datetime"] = new[]
            {
                M("now()", "Datetime"),
                M("newInstance()", "Datetime"),
                M("valueOf()", "Datetime"),
                M("newInstanceGmt()", "Datetime"),
                M("valueOfGmt()", "Datetime"),
            },
            ["Math"] = new[]
            {
                M("abs()", "Decimal"),
                M("round()", "Integer"),
                M("floor()", "Decimal"),
                M("ceil()", "Decimal"),
                M("mod()", "Integer"),
                M("pow()", "Double"),
                M("sqrt()", "Double"),
                M("min()", "Decimal"),
                M("max()", "Decimal"),
                M("random()", "Double"),
            },
            ["String"] = new[]
            {
                M("valueOf()", "String"),
                M("isEmpty()", "Boolean"),
                M("isBlank()", "Boolean"),
                M("isNotBlank()", "Boolean"),
                M("join()", "String"),
                M("split()", "List<String>"),
                M("format()", "String"),
                M("escapeSingleQuotes()", "String"),
                M("fromCharArray()", "String"),
            },
            ["Integer"] = new[]
            {
                M("valueOf()", "Integer"),
            },
            ["Decimal"] = new[]
            {
                M("valueOf()", "Decimal"),
            },
            ["Trigger"] = new[]
            {
                M("new", "SObject"),
                M("old", "SObject"),
                M("newMap", "Map<Id, SObject>"),
                M("oldMap", "Map<Id, SObject>"),
                M("isExecuting", "Boolean"),
                M("isBefore", "Boolean"),
                M("isAfter", "Boolean"),
                M("isInsert", "Boolean"),
                M("isUpdate", "Boolean"),
                M("isDelete", "Boolean"),
                M("isUndelete", "Boolean"),
                M("size", "Integer"),
            },
            ["ApexPages"] = new[]
            {
                M("addMessage()", "void"),
                M("currentPage()", "PageReference"),
                M("hasMessages()", "Boolean"),
            },
            ["Messaging"] = new[]
            {
                M("sendEmail()", "Messaging.SendEmailResult"),
            },
            ["Schema"] = new[]
            {
                M("getGlobalDescribe()", "Map<String, SObjectType>"),
                M("getSObjectType()", "SObjectType"),
            },
        };

    /// <summary>単語入力の候補（テンプレート・キーワード・型・静的クラス）を返す。</summary>
    public static IReadOnlyList<SoqlCompletionItem> Snippets(string prefix)
    {
        var items = new List<SoqlCompletionItem>();

        foreach (var (text, description, cursor) in Templates)
        {
            if (StartsWith(text, prefix))
            {
                items.Add(new SoqlCompletionItem(text, description, cursor - text.Length));
            }
        }

        AddPlain(items, Types, prefix);
        AddPlain(items, KeywordsAndClasses, prefix);
        return items.Take(MaxItems).ToList();
    }

    /// <summary>静的クラス（System / Database / Test …）のメンバー候補を返す。</summary>
    public static IReadOnlyList<SoqlCompletionItem> StaticMembers(string root, string prefix)
    {
        if (!Members.TryGetValue(root, out var members))
        {
            return Array.Empty<SoqlCompletionItem>();
        }

        return members
            .Where(m => StartsWith(m.Text, prefix))
            .Take(MaxItems)
            .Select(m => new SoqlCompletionItem(m.Text, m.Description))
            .ToList();
    }

    /// <summary>静的クラス名として既知か（System / Database …）。</summary>
    public static bool HasStaticClass(string root) => Members.ContainsKey(root);

    /// <summary>sObject 変数のインスタンスメソッド候補を返す。</summary>
    public static IReadOnlyList<SoqlCompletionItem> SObjectMethods(string prefix) =>
        SObjectInstanceMembers
            .Where(m => StartsWith(m.Text, prefix))
            .Take(MaxItems)
            .Select(m => new SoqlCompletionItem(m.Text, m.Description))
            .ToList();

    /// <summary>
    /// 宣言された型からコレクション種別を取り出す（List&lt;Account&gt; → List / Account、Map&lt;Id, Account&gt; → Map / Account）。
    /// </summary>
    public static bool TryParseCollectionType(string declaredType, out string kind, out string elementType)
    {
        kind = string.Empty;
        elementType = string.Empty;

        var text = declaredType.Trim();
        var lt = text.IndexOf('<');
        if (lt < 0 || !text.EndsWith('>'))
        {
            return false;
        }

        var kindName = text[..lt].Trim();
        if (kindName.Equals("List", StringComparison.OrdinalIgnoreCase))
        {
            kind = "List";
        }
        else if (kindName.Equals("Set", StringComparison.OrdinalIgnoreCase))
        {
            kind = "Set";
        }
        else if (kindName.Equals("Map", StringComparison.OrdinalIgnoreCase))
        {
            kind = "Map";
        }
        else
        {
            return false;
        }

        var inner = text[(lt + 1)..^1].Trim();
        var comma = inner.LastIndexOf(',');
        elementType = (comma >= 0 ? inner[(comma + 1)..] : inner).Trim();
        return elementType.Length > 0;
    }

    /// <summary>コレクション変数のメソッド候補を返す。</summary>
    public static IReadOnlyList<SoqlCompletionItem> CollectionMethods(string kind, string prefix)
    {
        var source = kind.Equals("Set", StringComparison.OrdinalIgnoreCase) ? SetMethods
            : kind.Equals("Map", StringComparison.OrdinalIgnoreCase) ? MapMethods
            : ListMethods;

        return source
            .Where(m => StartsWith(m.Text, prefix))
            .Take(MaxItems)
            .Select(m => new SoqlCompletionItem(m.Text, m.Description))
            .ToList();
    }

    private static readonly IReadOnlyList<(string Text, string Description)> SObjectInstanceMembers = new[]
    {
        ("addError()", "void"),
        ("get()", "Object"),
        ("put()", "Object"),
        ("clone()", "SObject"),
        ("getSObjectType()", "SObjectType"),
    };

    private static readonly IReadOnlyList<(string Text, string Description)> ListMethods = new[]
    {
        ("add()", "void"),
        ("addAll()", "void"),
        ("size()", "Integer"),
        ("get()", "SObject"),
        ("set()", "void"),
        ("remove()", "SObject"),
        ("isEmpty()", "Boolean"),
        ("clear()", "void"),
        ("sort()", "void"),
        ("contains()", "Boolean"),
        ("indexOf()", "Integer"),
        ("clone()", "List"),
        ("iterator()", "Iterator"),
    };

    private static readonly IReadOnlyList<(string Text, string Description)> SetMethods = new[]
    {
        ("add()", "Boolean"),
        ("addAll()", "Boolean"),
        ("size()", "Integer"),
        ("contains()", "Boolean"),
        ("containsAll()", "Boolean"),
        ("remove()", "Boolean"),
        ("removeAll()", "Boolean"),
        ("isEmpty()", "Boolean"),
        ("clear()", "void"),
        ("clone()", "Set"),
        ("iterator()", "Iterator"),
    };

    private static readonly IReadOnlyList<(string Text, string Description)> MapMethods = new[]
    {
        ("get()", "Object"),
        ("put()", "Object"),
        ("putAll()", "void"),
        ("containsKey()", "Boolean"),
        ("remove()", "Object"),
        ("keySet()", "Set"),
        ("values()", "List"),
        ("size()", "Integer"),
        ("isEmpty()", "Boolean"),
        ("clear()", "void"),
        ("clone()", "Map"),
    };

    private static void AddPlain(List<SoqlCompletionItem> items, IReadOnlyList<(string Text, string Description, int Cursor)> source, string prefix)
    {
        foreach (var (text, description, cursor) in source)
        {
            if (StartsWith(text, prefix))
            {
                items.Add(new SoqlCompletionItem(text, description, cursor - text.Length));
            }
        }
    }

    private static (string Text, string Description, int Cursor) T(string template, string description)
    {
        const string Marker = "|";
        var cursor = template.IndexOf(Marker, StringComparison.Ordinal);
        return (template.Replace(Marker, string.Empty), description, cursor);
    }

    private static (string Text, string Description) M(string text, string description) => (text, description);

    private static bool StartsWith(string value, string prefix) =>
        prefix.Length == 0 || value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
