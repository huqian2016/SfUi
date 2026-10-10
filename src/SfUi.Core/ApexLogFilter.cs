using System.Text;

namespace SfUi.Core;

/// <summary>匿名 Apex のデバッグ ログから表示用の行を抽出する純関数。</summary>
public static class ApexLogFilter
{
    /// <summary>System.debug() の出力行（USER_DEBUG）だけを残した本文を返す。</summary>
    public static string FilterUserDebugLines(string? logs)
    {
        if (string.IsNullOrEmpty(logs))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var raw in logs.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Contains("|USER_DEBUG|", StringComparison.Ordinal))
            {
                builder.Append(line).Append('\n');
            }
        }

        return builder.ToString().TrimEnd('\n');
    }
}
