using System.Diagnostics;

namespace SfUi.Core;

/// <summary>
/// レポートを PDF へ変換する。HTML を生成し、インストール済みの Chromium 系ブラウザー
/// （Microsoft Edge / Google Chrome など）のヘッドレス印刷機能で PDF を作る。
/// 追加のライブラリ依存を持たず、日本語もブラウザーのフォントで正しく描画される。
/// </summary>
public static class ReportPdfWriter
{
    /// <summary>HTML 文字列を PDF ファイルへ書き出す。</summary>
    public static async Task WriteAsync(string html, string pdfPath, CancellationToken cancellationToken = default)
    {
        var browser = FindBrowser()
            ?? throw new ReportExportException(UiText.T("Report_NoPdfBrowser"));

        var fullPdf = Path.GetFullPath(pdfPath);
        var directory = Path.GetDirectoryName(fullPdf);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempHtml = Path.Combine(Path.GetTempPath(), "sfui-report-" + Guid.NewGuid().ToString("N") + ".html");
        await File.WriteAllTextAsync(tempHtml, html, new System.Text.UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (File.Exists(fullPdf))
            {
                File.Delete(fullPdf);
            }

            var startInfo = new ProcessStartInfo(browser)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            startInfo.ArgumentList.Add("--headless=new");
            startInfo.ArgumentList.Add("--disable-gpu");
            startInfo.ArgumentList.Add("--no-pdf-header-footer");
            startInfo.ArgumentList.Add("--print-to-pdf-no-header");
            startInfo.ArgumentList.Add("--print-to-pdf=" + fullPdf);
            startInfo.ArgumentList.Add(new Uri(tempHtml).AbsoluteUri);

            using var process = Process.Start(startInfo)
                ?? throw new ReportExportException(UiText.T("Report_NoPdfBrowser"));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // プロセスが既に終了している場合は無視
                }

                throw new ReportExportException(UiText.T("Report_PdfTimeout"));
            }

            if (!File.Exists(fullPdf) || new FileInfo(fullPdf).Length == 0)
            {
                throw new ReportExportException(UiText.T("Report_PdfFailed"));
            }
        }
        finally
        {
            try
            {
                File.Delete(tempHtml);
            }
            catch
            {
                // 一時ファイルの削除失敗は無視
            }
        }
    }

    /// <summary>Chromium 系ブラウザーの実行ファイルを探す（見つからなければ null）。</summary>
    public static string? FindBrowser()
    {
        foreach (var candidate in Candidates())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        // PATH 上のコマンド名（macOS / Linux 用）
        if (!OperatingSystem.IsWindows())
        {
            foreach (var name in new[] { "msedge", "google-chrome", "chromium", "chromium-browser" })
            {
                var resolved = ResolveFromPath(name);
                if (resolved is not null)
                {
                    return resolved;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string?> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe");
            yield return Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe");
            yield return Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe");
            yield return Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe");
            yield return Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe");
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";
            yield return "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
            yield return "/Applications/Chromium.app/Contents/MacOS/Chromium";
        }
    }

    private static string? ResolveFromPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
