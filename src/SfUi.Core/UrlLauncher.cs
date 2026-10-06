using System.Diagnostics;

namespace SfUi.Core;

/// <summary>既定のブラウザ／メール クライアントで URL を開くヘルパー（About ウィンドウなどのリンク用）。</summary>
public static class UrlLauncher
{
    /// <summary>URL を開く。成功したら true（失敗しても例外は投げない）。</summary>
    public static bool TryOpen(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            if (PlatformInfo.IsWindows)
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            else
            {
                var startInfo = new ProcessStartInfo { FileName = "open", UseShellExecute = false };
                startInfo.ArgumentList.Add(url);
                Process.Start(startInfo);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
