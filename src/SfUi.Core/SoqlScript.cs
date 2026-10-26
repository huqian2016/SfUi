using System.Text;

namespace SfUi.Core;

/// <summary>スクリプト中の 1 文（コメントを除いた実行用の SOQL）。</summary>
public sealed record SoqlStatement(int Index, string Text);

/// <summary>
/// SOQL スクリプトの前処理。<c>--</c> 行コメント / <c>/* */</c> ブロックコメントを除去し、
/// <c>;</c> で複数の SOQL に分割する（文字列リテラル内の <c>;</c> は区切らない）。
/// </summary>
public static class SoqlScript
{
    /// <summary>1 回の実行で扱う最大文数（結果枠の上限）。</summary>
    public const int MaxStatements = 20;

    /// <summary>コメントだけ・空行だけの場合は空文字を返す。</summary>
    public static string StripComments(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var i = 0;
        var inString = false;
        while (i < text.Length)
        {
            var ch = text[i];
            if (inString)
            {
                builder.Append(ch);
                if (ch == '\\' && i + 1 < text.Length)
                {
                    builder.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                if (ch == '\'')
                {
                    inString = false;
                }

                i++;
                continue;
            }

            if (ch == '\'')
            {
                inString = true;
                builder.Append(ch);
                i++;
                continue;
            }

            if (ch == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                // 行コメント: 改行まで読み飛ばす（改行は残して行の区切りを保つ）
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                {
                    i++;
                }

                i = Math.Min(text.Length, i + 2);
                builder.Append(' ');
                continue;
            }

            builder.Append(ch);
            i++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// スクリプトを <c>;</c> で分割する（コメントは除去済み）。空の文は無視し、最大 <paramref name="maxStatements"/> 件まで返す。
    /// </summary>
    public static IReadOnlyList<SoqlStatement> Split(string? text, int maxStatements = MaxStatements)
    {
        var statements = new List<SoqlStatement>();
        if (string.IsNullOrWhiteSpace(text) || maxStatements <= 0)
        {
            return statements;
        }

        var builder = new StringBuilder(text.Length);
        var inString = false;
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];

            if (inString)
            {
                builder.Append(ch);
                if (ch == '\\' && i + 1 < text.Length)
                {
                    builder.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                if (ch == '\'')
                {
                    inString = false;
                }

                i++;
                continue;
            }

            if (ch == '\'')
            {
                inString = true;
                builder.Append(ch);
                i++;
                continue;
            }

            if (ch == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                {
                    i++;
                }

                i = Math.Min(text.Length, i + 2);
                builder.Append(' ');
                continue;
            }

            if (ch == ';')
            {
                AddStatement(statements, builder, maxStatements);
                builder.Clear();
                i++;
                continue;
            }

            builder.Append(ch);
            i++;
        }

        AddStatement(statements, builder, maxStatements);
        return statements;
    }

    private static void AddStatement(List<SoqlStatement> statements, StringBuilder builder, int maxStatements)
    {
        if (statements.Count >= maxStatements)
        {
            return;
        }

        var text = builder.ToString().Trim();
        if (text.Length == 0)
        {
            return;
        }

        statements.Add(new SoqlStatement(statements.Count, text));
    }
}
