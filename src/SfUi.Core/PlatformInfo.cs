namespace SfUi.Core;

/// <summary>
/// OS 判定とプラットフォーム差分の小さなヘルパー。
/// Core 内で OS を判定してよいのは PlatformInfo / SfCliRunner / ToolLauncherService / AppPaths に限定する
/// （それ以外のロジックは OS 非依存を維持する）。
/// </summary>
public static class PlatformInfo
{
    /// <summary>Windows か（WPF アプリ / プリプロセッサに依存しない実行時判定）。</summary>
    public static bool IsWindows { get; } = OperatingSystem.IsWindows();

    /// <summary>macOS か。</summary>
    public static bool IsMacOS { get; } = OperatingSystem.IsMacOS();

    /// <summary>PATH 環境変数の区切り文字（Windows = ';' / その他 = ':'）。</summary>
    public static char PathListSeparator => IsWindows ? ';' : ':';

    /// <summary>UI で表示する修飾キー名（Windows = Ctrl / macOS = Cmd）。</summary>
    public static string ShortcutModifierLabel => IsWindows ? "Ctrl" : "Cmd";
}

/// <summary>PATH 環境変数の走査（OS 別の区切り文字・ファイル名で検索する共通ヘルパー）。</summary>
public static class PathSearch
{
    /// <summary>
    /// <paramref name="pathVariable"/> を <paramref name="separator"/> で分割し、
    /// <paramref name="fileNames"/> のいずれかが存在する最初のフルパスを返す（未検出は null）。
    /// </summary>
    public static string? Find(
        string? pathVariable,
        IEnumerable<string> fileNames,
        char separator,
        Func<string, bool>? fileExists = null)
    {
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        var exists = fileExists ?? File.Exists;
        var names = fileNames.ToArray();
        foreach (var raw in pathVariable.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = raw.Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            foreach (var fileName in names)
            {
                try
                {
                    var candidate = Path.Combine(directory, fileName);
                    if (exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception)
                {
                    // 不正な PATH 要素は無視
                }
            }
        }

        return null;
    }
}
