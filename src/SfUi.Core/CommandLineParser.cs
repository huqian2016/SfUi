using System.Text;

namespace SfUi.Core;

/// <summary>コマンドライン文字列の分割（引用符対応）。</summary>
public static class CommandLineParser
{
    /// <summary>引用符（' と "）を考慮してトークンへ分割する。</summary>
    public static IReadOnlyList<string> Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return tokens;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        foreach (var c in commandLine)
        {
            if (inQuotes)
            {
                if (c == quoteChar)
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                inQuotes = true;
                quoteChar = c;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>先頭の sf / sf.cmd / sf.exe を除去した引数リストを返す。</summary>
    public static IReadOnlyList<string> SplitSfArguments(string commandLine)
    {
        var tokens = Tokenize(commandLine).ToList();
        if (tokens.Count > 0 && IsSfCommand(tokens[0]))
        {
            tokens.RemoveAt(0);
        }

        return tokens;
    }

    private static bool IsSfCommand(string token) =>
        token.Equals("sf", StringComparison.OrdinalIgnoreCase)
        || token.Equals("sf.cmd", StringComparison.OrdinalIgnoreCase)
        || token.Equals("sf.exe", StringComparison.OrdinalIgnoreCase);
}
