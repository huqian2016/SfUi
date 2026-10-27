namespace SfUi.Core;

/// <summary>
/// 予約実行ジョブの入出力パスを解決する。
/// 相対パスは「プロセスの作業ディレクトリ」ではなく <c>data/exports/</c> を基準にする
/// （起動方法によって保存先が変わらないようにするため）。
/// </summary>
public static class JobPathResolver
{
    /// <summary>
    /// 出力パスを絶対パスに解決する。空文字 / null は空文字を返す。
    /// 相対パスは <paramref name="paths"/> の <see cref="AppPaths.ExportsDirectory"/> 配下に展開する。
    /// </summary>
    public static string ResolveOutput(string? path, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(paths.ExportsDirectory, trimmed));
    }
}
