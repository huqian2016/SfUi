namespace SfUi.Core;

/// <summary>未反映行の種類（エディタの行ハイライト用）。</summary>
public enum LineChangeKind
{
    /// <summary>追加された行（緑）。</summary>
    Added,

    /// <summary>変更された行・削除境界の行（黄）。</summary>
    Modified,
}

/// <summary>
/// 行単位の簡易 diff（baseline = 組織と一致した内容 vs working = 編集中の内容）。未反映行のハイライトに使う純関数。
/// アルゴリズム: 共通の前後行を除去 → 残りを LCS で比較 → 置換ブロックの挿入行は Modified、単独挿入は Added、
/// 削除のみのブロックは境界行を Modified にする。巨大な差分（中間が 1000×1000 セル超）は中間全体を Modified に丸める。
/// 返り値: working 側の 1 始まり行番号 → 種別。
/// </summary>
public static class LineDiff
{
    private const long MaxCells = 1_000_000;

    /// <summary>テキストを行配列へ分割する（末尾の改行は行数を増やさない。空文字は 0 行）。</summary>
    public static string[] SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        return lines;
    }

    /// <summary>baseline に対する working の未反映行を計算する（working の 1 始まり行番号 → 種別）。</summary>
    public static IReadOnlyDictionary<int, LineChangeKind> Compute(string? baseline, string? working)
    {
        var result = new Dictionary<int, LineChangeKind>();
        var a = SplitLines(baseline);
        var b = SplitLines(working);
        if (a.AsSpan().SequenceEqual(b.AsSpan()))
        {
            return result;
        }

        // 共通の前後行を除去
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && string.Equals(a[prefix], b[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix
               && string.Equals(a[a.Length - 1 - suffix], b[b.Length - 1 - suffix], StringComparison.Ordinal))
        {
            suffix++;
        }

        var midA = a[prefix..(a.Length - suffix)];
        var midB = b[prefix..(b.Length - suffix)];
        if (midA.Length == 0 && midB.Length == 0)
        {
            return result;
        }

        // 削除のみ → 境界行を Modified に
        if (midB.Length == 0)
        {
            if (b.Length > 0)
            {
                var line = Math.Clamp(prefix == 0 ? 1 : prefix, 1, b.Length);
                result[line] = LineChangeKind.Modified;
            }

            return result;
        }

        // 挿入のみ → すべて Added
        if (midA.Length == 0)
        {
            for (var i = 0; i < midB.Length; i++)
            {
                result[prefix + i + 1] = LineChangeKind.Added;
            }

            return result;
        }

        // 大きすぎる差分は中間全体を Modified に丸める
        if ((long)(midA.Length + 1) * (midB.Length + 1) > MaxCells)
        {
            for (var i = 0; i < midB.Length; i++)
            {
                result[prefix + i + 1] = LineChangeKind.Modified;
            }

            return result;
        }

        // LCS テーブル
        var n = midA.Length;
        var m = midB.Length;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(midA[i], midB[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        // 逆順トレース（削除を優先して決定的にする）。種別: 0 = 一致 / 1 = 削除 / 2 = 挿入
        var ops = new List<(int Kind, int AIndex, int BIndex)>();
        var x = 0;
        var y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && string.Equals(midA[x], midB[y], StringComparison.Ordinal))
            {
                ops.Add((0, x, y));
                x++;
                y++;
            }
            else if (x < n && (y >= m || lcs[x + 1, y] >= lcs[x, y + 1]))
            {
                ops.Add((1, x, -1));
                x++;
            }
            else
            {
                ops.Add((2, -1, y));
                y++;
            }
        }

        // 操作列から working 行の種別を決める
        var pendingDelete = false;
        var blockPaired = false;
        var bConsumed = 0;
        foreach (var (kind, _, bIndex) in ops)
        {
            switch (kind)
            {
                case 1: // 削除
                    pendingDelete = true;
                    break;
                case 2: // 挿入（直前が削除なら置換 = Modified）
                    result[prefix + bIndex + 1] = pendingDelete ? LineChangeKind.Modified : LineChangeKind.Added;
                    if (pendingDelete)
                    {
                        blockPaired = true;
                    }

                    bConsumed = bIndex + 1;
                    break;
                default: // 一致（保留削除が挿入と対になっていなければ削除境界マーカー）
                    if (pendingDelete && !blockPaired)
                    {
                        AnchorDelete(result, b, bIndex, prefix);
                    }

                    pendingDelete = false;
                    blockPaired = false;
                    bConsumed = bIndex + 1;
                    break;
            }
        }

        if (pendingDelete && !blockPaired)
        {
            AnchorDelete(result, b, bConsumed, prefix);
        }

        return result;
    }

    /// <summary>削除のみのブロックの境界マーカーを立てる（working の直前の行。先頭での削除は先頭行）。</summary>
    private static void AnchorDelete(Dictionary<int, LineChangeKind> result, string[] workingLines, int insertionPoint, int prefix)
    {
        if (workingLines.Length == 0)
        {
            return;
        }

        var line = insertionPoint == 0 ? prefix + 1 : prefix + insertionPoint;
        line = Math.Clamp(line, 1, workingLines.Length);
        if (!result.ContainsKey(line))
        {
            result[line] = LineChangeKind.Modified;
        }
    }
}
