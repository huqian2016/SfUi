using System.Text;

namespace SfUi.Core;

/// <summary>
/// 帳票を指定された形式すべてへ書き出す。
/// - HTML / PDF（印刷用。PDF はインストール済みの Chromium 系ブラウザーで印刷）
/// - SVG / PNG / JPEG（1 ページ 1 ファイル）
/// </summary>
public static class FormOutputWriter
{
    /// <summary>帳票を書き出す（作成したファイルのパスを返す）。</summary>
    public static async Task<ReportExportResult> WriteAsync(
        FormDefinition form,
        FormData data,
        ReportFormats formats,
        string directory,
        string baseName,
        int imageDpi = FormImageWriter.DefaultDpi,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(data);

        var files = new List<string>();
        if (formats == ReportFormats.None)
        {
            return new ReportExportResult { Files = files };
        }

        directory = string.IsNullOrWhiteSpace(directory) ? Environment.CurrentDirectory : directory;
        Directory.CreateDirectory(directory);
        baseName = string.IsNullOrWhiteSpace(baseName) ? "form-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") : baseName;

        var html = ReportFormats.None;
        if (formats.HasFlag(ReportFormats.Html) || formats.HasFlag(ReportFormats.Pdf))
        {
            html = formats;
            var content = FormHtmlWriter.Write(form, data);
            if (formats.HasFlag(ReportFormats.Html))
            {
                var path = Path.Combine(directory, baseName + ".html");
                await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                files.Add(path);
            }

            if (formats.HasFlag(ReportFormats.Pdf))
            {
                var path = Path.Combine(directory, baseName + ".pdf");
                await ReportPdfWriter.WriteAsync(content, path, cancellationToken).ConfigureAwait(false);
                files.Add(path);
            }
        }

        _ = html;

        var pages = FormDrawList.Build(form, data);

        if (formats.HasFlag(ReportFormats.Svg))
        {
            var svgs = FormSvgWriter.WritePages(form, data);
            for (var i = 0; i < svgs.Count; i++)
            {
                var path = Path.Combine(directory, PageFileName(baseName, i, svgs.Count, ".svg"));
                await File.WriteAllTextAsync(path, svgs[i], new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                files.Add(path);
            }
        }

        if (formats.HasFlag(ReportFormats.Png) || formats.HasFlag(ReportFormats.Jpeg))
        {
            for (var i = 0; i < pages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (formats.HasFlag(ReportFormats.Png))
                {
                    var path = Path.Combine(directory, PageFileName(baseName, i, pages.Count, ".png"));
                    await File.WriteAllBytesAsync(path, FormImageWriter.RenderPage(pages[i], ReportImageFormat.Png, imageDpi), cancellationToken)
                        .ConfigureAwait(false);
                    files.Add(path);
                }

                if (formats.HasFlag(ReportFormats.Jpeg))
                {
                    var path = Path.Combine(directory, PageFileName(baseName, i, pages.Count, ".jpg"));
                    await File.WriteAllBytesAsync(path, FormImageWriter.RenderPage(pages[i], ReportImageFormat.Jpeg, imageDpi), cancellationToken)
                        .ConfigureAwait(false);
                    files.Add(path);
                }
            }
        }

        return new ReportExportResult { Files = files };
    }

    private static string PageFileName(string baseName, int index, int pageCount, string extension) =>
        pageCount <= 1
            ? baseName + extension
            : baseName + "-p" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + extension;
}
