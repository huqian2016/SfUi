using System.Text.RegularExpressions;

namespace SfUi.Core;

/// <summary>
/// 入力中の SOQL から件数取得用の COUNT() クエリを作る（純関数・テスト対象）。
/// 件数のライブ表示に使う。作れない場合（FROM なし・GROUP BY / TYPEOF あり等）は null。
/// </summary>
public static class SoqlCountQueryBuilder
{
    /// <summary>COUNT() では無効な句（見つかった位置以降を切り落とす）。</summary>
    private static readonly Regex CutoffPattern = new(
        @"\b(ORDER\s+BY|LIMIT|OFFSET|FOR)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>件数を取得できない構文（そのままでは COUNT 化しない）。</summary>
    private static readonly Regex UnsupportedPattern = new(
        @"\b(GROUP\s+BY|TYPEOF)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FromPattern = new(@"\bFROM\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? Build(string? soql)
    {
        if (string.IsNullOrWhiteSpace(soql))
        {
            return null;
        }

        var text = soql.Trim().TrimEnd(';').TrimEnd();
        var sanitized = SoqlCompletionParser.StripLiterals(text);

        var fromMatch = FromPattern.Match(sanitized);
        if (!fromMatch.Success || fromMatch.Index == 0)
        {
            return null;
        }

        var tail = text[fromMatch.Index..];                    // "FROM ..."（元の大小文字を保持）
        var tailSanitized = sanitized[fromMatch.Index..];
        if (UnsupportedPattern.IsMatch(tailSanitized))
        {
            return null;
        }

        var cutoff = CutoffPattern.Match(tailSanitized);
        if (cutoff.Success)
        {
            tail = tail[..cutoff.Index];
        }

        tail = tail.TrimEnd();
        return tail.Length == 0 ? null : "SELECT COUNT() " + tail;
    }
}
