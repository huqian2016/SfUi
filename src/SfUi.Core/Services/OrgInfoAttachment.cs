using System.Text;

namespace SfUi.Core;

/// <summary>
/// AI パネルへ添付するタブデータのテキスト整形（タイトル行 + ヘッダー行 + TSV 行、最大文字数で切り詰め）。
/// タブ・改行は空白へ置換して 1 行 1 レコードを保つ。
/// </summary>
public static class OrgInfoAttachment
{
    public const int DefaultMaxChars = 12000;

    public static string Build(string titleLine, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, int maxChars = DefaultMaxChars)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Sanitize(titleLine));
        builder.AppendLine(string.Join('\t', headers.Select(Sanitize)));

        var appended = 0;
        foreach (var row in rows)
        {
            var line = string.Join('\t', row.Select(Sanitize));
            if (builder.Length + line.Length + 2 > maxChars)
            {
                break;
            }

            builder.AppendLine(line);
            appended++;
        }

        if (appended < rows.Count)
        {
            builder.Append(UiText.T("OrgInfo_TruncatedFmt", appended, rows.Count));
        }

        return builder.ToString().TrimEnd();
    }

    private static string Sanitize(string? value) =>
        (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
