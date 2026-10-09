using System.Globalization;
using System.Text.RegularExpressions;

namespace SfUi.Etl.Sources;

/// <summary>
/// SOQL delta のプッシュダウン: タイムスタンプ条件（例: <c>LastModifiedDate &gt; watermark</c>）を
/// SOQL 本体の WHERE 句へ注入し、変更分だけを組織から取得する（クライアント側フィルタの全件読み込みを避ける）。
/// WHERE が無ければ追加し、既存の WHERE は括弧で包んで AND 条件として合成する。
/// ORDER BY / GROUP BY / LIMIT / FOR / WITH などの後続クローズは維持し、USING SCOPE の後ろに置く。
/// 解析できない・列名が不正・watermark なしの場合は false（呼び出し側は従来のクライアント側フィルタへフォールバック）。
/// </summary>
public static class SoqlDeltaPushDown
{
    private static readonly Regex ColumnPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$",
        RegexOptions.Compiled);

    /// <summary>差分条件を SOQL へ注入する（成功時は filteredSoql に注入済みクエリを返す）。</summary>
    public static bool TryBuild(string? soql, string deltaColumn, DateTimeOffset? watermark, out string filteredSoql)
    {
        filteredSoql = (soql ?? string.Empty).Trim();
        if (filteredSoql.Length == 0 || watermark is null)
        {
            return false;
        }

        var column = (deltaColumn ?? string.Empty).Trim();
        if (!ColumnPattern.IsMatch(column))
        {
            return false;
        }

        var scan = Scan(filteredSoql);
        if (scan is null)
        {
            return false;
        }

        var condition = BuildCondition(column, watermark.Value);
        if (scan.Value.WhereEnd >= 0)
        {
            var body = filteredSoql[scan.Value.WhereEnd..scan.Value.InsertAt].Trim();
            if (body.Length == 0)
            {
                return false;
            }

            var tail = scan.Value.InsertAt < filteredSoql.Length
                ? " " + filteredSoql[scan.Value.InsertAt..].TrimStart()
                : string.Empty;
            filteredSoql = filteredSoql[..scan.Value.WhereEnd].TrimEnd()
                + " (" + body + ") AND (" + condition + ")" + tail;
        }
        else
        {
            var tail = scan.Value.InsertAt < filteredSoql.Length
                ? " " + filteredSoql[scan.Value.InsertAt..].TrimStart()
                : string.Empty;
            filteredSoql = filteredSoql[..scan.Value.InsertAt].TrimEnd() + " WHERE " + condition + tail;
        }

        return true;
    }

    /// <summary>差分条件（<c>列 &gt; リテラル</c>）を作る。</summary>
    public static string BuildCondition(string deltaColumn, DateTimeOffset watermark)
        => $"{deltaColumn.Trim()} > {FormatLiteral(watermark)}";

    /// <summary>watermark を SOQL の日時リテラル（UTC・ミリ秒付き）へ整形する。</summary>
    public static string FormatLiteral(DateTimeOffset watermark)
        => watermark.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private readonly record struct ScanResult(int WhereEnd, int InsertAt);

    /// <summary>
    /// トップレベル（括弧・文字列リテラルの外）を走査し、WHERE の終端と注入位置を求める。
    /// 注入位置 = WHERE より後の最初のクローズ（ORDER BY / GROUP BY / LIMIT など）の先頭。無ければ末尾。
    /// USING SCOPE は WHERE より前に来るクローズのため読み飛ばす。
    /// </summary>
    private static ScanResult? Scan(string soql)
    {
        var depth = 0;
        var inString = false;
        var whereEnd = -1;
        var insertAt = -1;

        for (var i = 0; i < soql.Length; i++)
        {
            var c = soql[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;   // エスケープ（\' など）
                }
                else if (c == '\'')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '\'')
            {
                inString = true;
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                if (depth < 0)
                {
                    return null;
                }

                continue;
            }

            if (depth != 0 || (i > 0 && !char.IsWhiteSpace(soql[i - 1])))
            {
                continue;
            }

            if (MatchesWord(soql, i, "using"))
            {
                continue;   // USING SCOPE は WHERE より前 → 読み飛ばす（WHERE はその後ろへ）
            }

            if (whereEnd < 0 && MatchesWord(soql, i, "where"))
            {
                whereEnd = i + 5;
                continue;
            }

            if (MatchesWord(soql, i, "order") && MatchesFollowingWord(soql, i + 5, "by"))
            {
                insertAt = i;
                break;
            }

            if (MatchesWord(soql, i, "group") && MatchesFollowingWord(soql, i + 5, "by"))
            {
                insertAt = i;
                break;
            }

            if (MatchesWord(soql, i, "having")
                || MatchesWord(soql, i, "limit")
                || MatchesWord(soql, i, "offset")
                || MatchesWord(soql, i, "for")
                || MatchesWord(soql, i, "with"))
            {
                insertAt = i;
                break;
            }
        }

        if (inString)
        {
            return null;   // 引用符が閉じていない
        }

        return new ScanResult(whereEnd, insertAt < 0 ? soql.Length : insertAt);
    }

    /// <summary>現在位置が指定の語（前は空白済み前提。後ろは識別子文字以外か末尾）と一致するか。</summary>
    private static bool MatchesWord(string soql, int index, string word)
        => index + word.Length <= soql.Length
            && soql.AsSpan(index, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase)
            && (index + word.Length == soql.Length || !IsIdentifierChar(soql[index + word.Length]));

    /// <summary>空白を挟んで次の語が一致するか（ORDER BY / GROUP BY 用）。</summary>
    private static bool MatchesFollowingWord(string soql, int index, string word)
    {
        var i = index;
        if (i >= soql.Length || !char.IsWhiteSpace(soql[i]))
        {
            return false;
        }

        while (i < soql.Length && char.IsWhiteSpace(soql[i]))
        {
            i++;
        }

        return MatchesWord(soql, i, word);
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
