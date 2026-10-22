namespace SfUi.Core;

/// <summary>
/// 候補チップをクリックしたとき、続けて次の候補をクリックできるよう自動で付ける区切り（", "）の判定。
/// 純関数（テスト対象）。SELECT 句の項目候補だけを対象にする。
/// </summary>
public static class SoqlSuggestionInsertion
{
    /// <summary>自動で付ける区切り（不要なら空文字）。</summary>
    public const string FieldSeparator = ", ";

    /// <summary>
    /// この候補を挿入した直後に付ける区切りを返す。
    /// 対象は SELECT 句の項目候補のみで、次の場合は付けない:
    /// リレーション候補（末尾 "."）/ 関数の括弧内へ移動する候補 / 括弧の内側 / 次が既に "," か "." / ")"。
    /// </summary>
    public static string SeparatorFor(string? text, int caret, SoqlCompletionContext context, SoqlCompletionItem item)
    {
        text ??= string.Empty;
        caret = Math.Clamp(caret, 0, text.Length);

        if (context.InsideString || context.Clause != SoqlClause.Select)
        {
            return string.Empty;
        }

        // リレーション候補（Owner. など）と、カーソルを括弧内へ置く関数候補はパスの続きを入力する
        if (item.Text.EndsWith(".", StringComparison.Ordinal) || item.CaretOffsetDelta != 0)
        {
            return string.Empty;
        }

        // COUNT( の中など、括弧の内側ではカンマを付けない
        if (ParenDepth(text, Math.Clamp(context.WordStart, 0, text.Length)) > 0)
        {
            return string.Empty;
        }

        var next = NextSignificantChar(text, caret);
        if (next is ',' or '.' or ')')
        {
            return string.Empty;
        }

        // 後続が既に空白（例: "SELECT Na FROM"）ならカンマだけにして余分な空白を作らない
        var nextChar = caret < text.Length ? text[caret] : '\0';
        return nextChar != '\0' && char.IsWhiteSpace(nextChar) ? "," : FieldSeparator;
    }

    /// <summary>指定位置より前の括弧の深さ（文字列リテラル内は数えない）。</summary>
    public static int ParenDepth(string? text, int end)
    {
        text ??= string.Empty;
        end = Math.Clamp(end, 0, text.Length);
        var depth = 0;
        var inString = false;
        for (var i = 0; i < end; i++)
        {
            var c = text[i];
            if (c == '\'')
            {
                if (inString && i + 1 < end && text[i + 1] == '\'')
                {
                    i++; // '' はエスケープされた引用符
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && depth > 0)
            {
                depth--;
            }
        }

        return depth;
    }

    /// <summary>カーソル位置以降で最初の空白以外の文字（無ければ null）。</summary>
    public static char? NextSignificantChar(string? text, int caret)
    {
        text ??= string.Empty;
        for (var i = Math.Clamp(caret, 0, text.Length); i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return text[i];
            }
        }

        return null;
    }

    /// <summary>
    /// 挿入後のキャレット位置。カンマだけを付けたとき（後続に既存の空白があるとき）は、
    /// その空白を飛び越えて置く（連続クリックで "Name, Id" のように空白を保つ）。
    /// </summary>
    public static int CaretAfterInsert(string? text, int segmentStart, SoqlCompletionItem item, string suffix)
    {
        text ??= string.Empty;
        var position = segmentStart + item.Text.Length + suffix.Length + item.CaretOffsetDelta;
        if (suffix == "," && item.CaretOffsetDelta == 0)
        {
            while (position < text.Length && (text[position] == ' ' || text[position] == '\t'))
            {
                position++;
            }
        }

        return Math.Clamp(position, 0, text.Length);
    }
}
