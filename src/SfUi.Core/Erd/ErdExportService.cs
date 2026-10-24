using System.Diagnostics;
using System.Text;

namespace SfUi.Core;

/// <summary>ER 図の出力指定（形式は複数選択可）。</summary>
public sealed class ErdExportRequest
{
    public required ErdDocument Document { get; init; }

    public required string OutputDirectory { get; init; }

    /// <summary>拡張子を除いたファイル名。</summary>
    public required string BaseName { get; init; }

    public bool Mermaid { get; init; } = true;

    public bool Dbml { get; init; }

    public bool Drawio { get; init; }

    public bool PlantUml { get; init; }

    public bool Dot { get; init; }

    /// <summary>自己完結の HTML（図 + 表 + 検索）。</summary>
    public bool Html { get; init; }

    public bool Svg { get; init; }

    public bool Png { get; init; }

    public bool Jpeg { get; init; }

    public bool Webp { get; init; }

    public bool OpenFolder { get; init; }
}

/// <summary>ER 図の出力結果。</summary>
public sealed record ErdExportResult(IReadOnlyList<string> Files)
{
    public string? PrimaryFile => Files.Count == 0 ? null : Files[0];
}

/// <summary>ER 図を選択された形式で書き出す（テキスト 6 形式 + 画像 4 形式）。</summary>
public static class ErdExportService
{
    public static ErdExportResult Export(ErdExportRequest request)
    {
        var document = request.Document;
        if (document.Entities.Count == 0)
        {
            throw new ReportExportException(UiText.T("Erd_NoObjects"));
        }

        var directory = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.GetTempPath()
            : request.OutputDirectory;

        Directory.CreateDirectory(directory);
        var baseName = string.IsNullOrWhiteSpace(request.BaseName) ? "erd" : request.BaseName;
        var files = new List<string>();

        void WriteText(string extension, string content)
        {
            var path = Path.Combine(directory, baseName + "." + extension);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            files.Add(path);
        }

        if (request.Mermaid)
        {
            var mermaid = ErdDiagramWriter.WriteMermaid(document);
            WriteText("mmd", mermaid);
            WriteText("md", "# " + document.Title + "\n\n```mermaid\n" + mermaid + "```\n");
        }

        if (request.Dbml)
        {
            WriteText("dbml", ErdDiagramWriter.WriteDbml(document));
        }

        if (request.Drawio)
        {
            WriteText("drawio", ErdDiagramWriter.WriteDrawio(document));
        }

        if (request.PlantUml)
        {
            WriteText("puml", ErdDiagramWriter.WritePlantUml(document));
        }

        if (request.Dot)
        {
            WriteText("dot", ErdDiagramWriter.WriteDot(document));
        }

        if (request.Html)
        {
            WriteText("html", ErdDiagramWriter.WriteHtml(document));
        }

        if (request.Svg)
        {
            WriteText("svg", ErdImageRenderer.WriteSvg(document));
        }

        if (request.Png)
        {
            WriteBytes("png", ErdImageRenderer.Render(document, ErdImageFormat.Png));
        }

        if (request.Jpeg)
        {
            WriteBytes("jpg", ErdImageRenderer.Render(document, ErdImageFormat.Jpeg));
        }

        if (request.Webp)
        {
            WriteBytes("webp", ErdImageRenderer.Render(document, ErdImageFormat.Webp));
        }

        if (files.Count == 0)
        {
            throw new ReportExportException(UiText.T("Erd_NoFormats"));
        }

        if (request.OpenFolder)
        {
            OpenFolder(directory);
        }

        return new ErdExportResult(files);

        void WriteBytes(string extension, byte[] bytes)
        {
            var path = Path.Combine(directory, baseName + "." + extension);
            File.WriteAllBytes(path, bytes);
            files.Add(path);
        }
    }

    public static string DescribeResult(ErdExportResult result)
    {
        var folder = result.PrimaryFile is null ? string.Empty : Path.GetDirectoryName(result.PrimaryFile) ?? string.Empty;
        return UiText.T("Erd_DoneFmt", result.Files.Count.ToString("N0"), folder);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch
        {
            // フォルダーを開けなくても出力自体は成功している
        }
    }
}
