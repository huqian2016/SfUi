using DynamicExpresso;

namespace SfUi.Etl.Expressions;

/// <summary>
/// 式言語エンジン（DynamicExpresso ラッパー）。
/// <list type="bullet">
/// <item>ソース列参照: 有効な識別子ならベア名（例: <c>Name</c>）、それ以外は <c>[列名]</c>（例: <c>[Account.Id]</c>）</item>
/// <item>関数: 文字列 / 数値 / 日付 / 論理 / システム / 参照（設計 §3.6）</item>
/// <item>式は <see cref="Compile"/> で 1 回だけパースし、<see cref="CompiledExpression.EvaluateArgs"/> で高速に逐次評価できる</item>
/// </list>
/// </summary>
public sealed class ExpressionEngine
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        // 組み込み関数名（ベア参照させない。必要な場合は [名前] を使用）
        "TEXT", "LEFT", "RIGHT", "MID", "LEN", "TRIM", "UPPER", "LOWER", "REPLACE", "CONCAT", "SPLIT", "JOIN",
        "SPLIT_PART", "LPAD", "RPAD", "CONTAINS", "STARTSWITH", "ENDSWITH", "ID15", "ID18",
        "TO_NUMBER", "TO_INT", "ROUND", "ABS", "FLOOR", "CEIL", "MIN", "MAX", "IS_NUMBER", "FORMAT_NUMBER",
        "TO_DATE", "FORMAT_DATE", "ADD_DAYS", "ADD_MONTHS", "ADD_HOURS", "DIFF_DAYS", "YEAR", "MONTH", "DAY", "TO_UTC", "TODAY", "NOW",
        "IF", "IIF", "IFNULL", "COALESCE", "NULLIF", "ISBLANK",
        "CURRENT_USER", "CURRENT_ORG", "CURRENT_PC", "ROW_NUMBER", "GUID", "LOOKUP", "PREV", "PARENT",
        // C# 予約語
        "true", "false", "null", "if", "else", "for", "foreach", "while", "do", "switch", "case", "default",
        "new", "is", "as", "in", "this", "base", "var", "return", "break", "continue", "try", "catch", "finally",
        "int", "long", "short", "byte", "string", "bool", "double", "decimal", "float", "object", "char", "void",
    };

    private readonly ExpressionHost _host;

    public ExpressionEngine(ExpressionHost? host = null) => _host = host ?? new ExpressionHost();

    public ExpressionHost Host => _host;

    /// <summary>
    /// 式をコンパイルする。<paramref name="sourceColumns"/> は式から参照可能なソース列名。
    /// </summary>
    public CompiledExpression Compile(string expression, IEnumerable<string>? sourceColumns = null)
    {
        var interpreter = new Interpreter(InterpreterOptions.Default);
        RegisterFunctions(interpreter);

        var rewritten = expression ?? string.Empty;
        var variables = new List<string>();
        var parameters = new List<Parameter>();
        var index = 0;

        foreach (var column in sourceColumns ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrEmpty(column) || variables.Contains(column))
            {
                continue;
            }

            var isBareUsable = IsValidIdentifier(column) && !ReservedNames.Contains(column);
            var safeName = isBareUsable ? column : "__v" + index++;
            var token = "[" + column + "]";
            var hasBracket = rewritten.Contains(token, StringComparison.Ordinal);

            if (hasBracket)
            {
                rewritten = rewritten.Replace(token, safeName, StringComparison.Ordinal);
            }
            else if (!isBareUsable)
            {
                continue;   // ベア参照不可かつ [列名] も使われていない → 登録不要
            }

            variables.Add(column);
            parameters.Add(new Parameter(safeName, typeof(object), null));
        }

        var lambda = interpreter.Parse(rewritten, parameters.ToArray());
        return new CompiledExpression(expression ?? string.Empty, variables, lambda);
    }

    private void RegisterFunctions(Interpreter interpreter)
    {
        // 文字列
        interpreter.SetFunction("TEXT", (Func<object?, object?>)ExprFunctions.Text);
        interpreter.SetFunction("LEFT", (Func<object?, int, object?>)ExprFunctions.Left);
        interpreter.SetFunction("RIGHT", (Func<object?, int, object?>)ExprFunctions.Right);
        interpreter.SetFunction("MID", (Func<object?, int, int, object?>)ExprFunctions.Mid);
        interpreter.SetFunction("LEN", (Func<object?, int>)ExprFunctions.Len);
        interpreter.SetFunction("TRIM", (Func<object?, object?>)ExprFunctions.Trim);
        interpreter.SetFunction("UPPER", (Func<object?, object?>)ExprFunctions.Upper);
        interpreter.SetFunction("LOWER", (Func<object?, object?>)ExprFunctions.Lower);
        interpreter.SetFunction("REPLACE", (Func<object?, string, string, object?>)ExprFunctions.Replace);
        interpreter.SetFunction("CONCAT", (Func<object?, object?, object?>)((a, b) => ExprFunctions.Concat(a, b)));
        interpreter.SetFunction("CONCAT", (Func<object?, object?, object?, object?>)((a, b, c) => ExprFunctions.Concat(a, b, c)));
        interpreter.SetFunction("CONCAT", (Func<object?, object?, object?, object?, object?>)((a, b, c, d) => ExprFunctions.Concat(a, b, c, d)));
        interpreter.SetFunction("SPLIT", (Func<object?, string, object?>)((v, s) => ExprFunctions.Split(v, s)));
        interpreter.SetFunction("JOIN", (Func<string, object?, object?>)((s, v) => ExprFunctions.Join(s, v)));
        interpreter.SetFunction("SPLIT_PART", (Func<object?, string, int, object?>)ExprFunctions.SplitPart);
        interpreter.SetFunction("LPAD", (Func<object?, int, string, object?>)ExprFunctions.LPad);
        interpreter.SetFunction("RPAD", (Func<object?, int, string, object?>)ExprFunctions.RPad);
        interpreter.SetFunction("CONTAINS", (Func<object?, string, bool>)ExprFunctions.Contains);
        interpreter.SetFunction("STARTSWITH", (Func<object?, string, bool>)ExprFunctions.StartsWith);
        interpreter.SetFunction("ENDSWITH", (Func<object?, string, bool>)ExprFunctions.EndsWith);
        interpreter.SetFunction("ID15", (Func<object?, object?>)ExprFunctions.Id15);
        interpreter.SetFunction("ID18", (Func<object?, object?>)ExprFunctions.Id18);

        // 数値
        interpreter.SetFunction("TO_NUMBER", (Func<object?, object?>)ExprFunctions.ToNumber);
        interpreter.SetFunction("TO_INT", (Func<object?, object?>)ExprFunctions.ToInt);
        interpreter.SetFunction("ROUND", (Func<object?, int, object?>)ExprFunctions.Round);
        interpreter.SetFunction("ABS", (Func<object?, object?>)ExprFunctions.Abs);
        interpreter.SetFunction("FLOOR", (Func<object?, object?>)ExprFunctions.Floor);
        interpreter.SetFunction("CEIL", (Func<object?, object?>)ExprFunctions.Ceil);
        interpreter.SetFunction("MIN", (Func<object?, object?, object?>)ExprFunctions.Min);
        interpreter.SetFunction("MAX", (Func<object?, object?, object?>)ExprFunctions.Max);
        interpreter.SetFunction("IS_NUMBER", (Func<object?, bool>)ExprFunctions.IsNumber);
        interpreter.SetFunction("FORMAT_NUMBER", (Func<object?, string, object?>)ExprFunctions.FormatNumber);

        // 日付
        interpreter.SetFunction("TO_DATE", (Func<object?, object?>)(v => ExprFunctions.ToDate(v)));
        interpreter.SetFunction("FORMAT_DATE", (Func<object?, string, object?>)((v, f) => ExprFunctions.FormatDate(v, f)));
        interpreter.SetFunction("ADD_DAYS", (Func<object?, int, object?>)((v, n) => ExprFunctions.AddDays(v, n)));
        interpreter.SetFunction("ADD_MONTHS", (Func<object?, int, object?>)((v, n) => ExprFunctions.AddMonths(v, n)));
        interpreter.SetFunction("ADD_HOURS", (Func<object?, int, object?>)((v, n) => ExprFunctions.AddHours(v, n)));
        interpreter.SetFunction("DIFF_DAYS", (Func<object?, object?, object?>)ExprFunctions.DiffDays);
        interpreter.SetFunction("YEAR", (Func<object?, object?>)ExprFunctions.Year);
        interpreter.SetFunction("MONTH", (Func<object?, object?>)ExprFunctions.Month);
        interpreter.SetFunction("DAY", (Func<object?, object?>)ExprFunctions.Day);
        interpreter.SetFunction("TO_UTC", (Func<object?, object?>)ExprFunctions.ToUtc);

        // 論理
        interpreter.SetFunction("IF", (Func<bool, object?, object?, object?>)ExprFunctions.If);
        interpreter.SetFunction("IIF", (Func<bool, object?, object?, object?>)ExprFunctions.If);
        interpreter.SetFunction("IFNULL", (Func<object?, object?, object?>)ExprFunctions.IfNull);
        interpreter.SetFunction("COALESCE", (Func<object?, object?, object?>)ExprFunctions.Coalesce);
        interpreter.SetFunction("NULLIF", (Func<object?, object?, object?>)ExprFunctions.NullIf);
        interpreter.SetFunction("ISBLANK", (Func<object?, bool>)ExprFunctions.IsBlank);

        // システム
        interpreter.SetFunction("CURRENT_USER", (Func<object?>)(() => _host.UserName));
        interpreter.SetFunction("CURRENT_ORG", (Func<object?>)(() => _host.OrgName));
        interpreter.SetFunction("CURRENT_PC", (Func<object?>)(() => _host.MachineName));
        interpreter.SetFunction("NOW", (Func<object?>)(() => _host.NowProvider()));
        interpreter.SetFunction("TODAY", (Func<object?>)(() => _host.TodayProvider()));
        interpreter.SetFunction("ROW_NUMBER", (Func<int>)(() => _host.RowNumberProvider()));
        interpreter.SetFunction("GUID", (Func<string>)(() => _host.GuidProvider()));

        // 参照
        interpreter.SetFunction("LOOKUP", (Func<string, string, object?, object?>)((o, k, v) => _host.Lookup?.Invoke(o, k, v)));
        interpreter.SetFunction("PREV", (Func<string, object?>)(f => _host.Prev?.Invoke(f)));
        interpreter.SetFunction("PARENT", (Func<string, object?>)(f => _host.Parent?.Invoke(f)));
    }

    private static bool IsValidIdentifier(string name)
    {
        if (name.Length == 0) return false;
        if (!(char.IsLetter(name[0]) || name[0] == '_')) return false;
        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        }

        return true;
    }
}

/// <summary>コンパイル済み式。行ごとの評価は <see cref="EvaluateArgs"/> / <see cref="Evaluate"/> で行う。</summary>
public sealed class CompiledExpression
{
    private readonly Lambda _lambda;

    internal CompiledExpression(string source, IReadOnlyList<string> variables, Lambda lambda)
    {
        Source = source;
        Variables = variables;
        _lambda = lambda;
    }

    /// <summary>元の式文字列。</summary>
    public string Source { get; }

    /// <summary>参照しているソース列名（<see cref="EvaluateArgs"/> の引数順）。</summary>
    public IReadOnlyList<string> Variables { get; }

    /// <summary>位置引数で評価する（高速パス: エンジンは行の値配列をそのまま渡す）。</summary>
    public object? EvaluateArgs(params object?[] args) => _lambda.Invoke(args);

    /// <summary>名前付きの行値で評価する（利便 API）。</summary>
    public object? Evaluate(IReadOnlyDictionary<string, object?> variables)
    {
        var args = new object?[Variables.Count];
        for (var i = 0; i < args.Length; i++)
        {
            variables.TryGetValue(Variables[i], out args[i]);
        }

        return _lambda.Invoke(args);
    }
}
