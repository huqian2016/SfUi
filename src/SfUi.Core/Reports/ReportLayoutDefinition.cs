namespace SfUi.Core;

/// <summary>
/// 帳票レイアウト定義（将来の SVF 風帳票用。Phase 1–2 ではモデルとストアのみ）。
/// レポート結果（<see cref="ReportDocument"/>）をレイアウトに差し込んで PDF / HTML / 画像へ出力する構想。
/// </summary>
public sealed class ReportLayoutDefinition
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>用紙（A4 / A3 / Letter）。</summary>
    public string PaperSize { get; set; } = "A4";

    /// <summary>portrait / landscape。</summary>
    public string Orientation { get; set; } = "portrait";

    public double MarginMm { get; set; } = 12;

    /// <summary>ヘッダー領域（差し込みフィールド + 固定文字）。</summary>
    public ReportLayoutRegion? Header { get; set; }

    /// <summary>フッター領域（ページ番号など）。</summary>
    public ReportLayoutRegion? Footer { get; set; }

    /// <summary>明細領域（1 レコード = 1 明細として繰り返す）。</summary>
    public ReportLayoutRegion? Detail { get; set; }

    /// <summary>集計領域（小計・合計）。</summary>
    public ReportLayoutRegion? Summary { get; set; }

    /// <summary>改ページ時にヘッダーを繰り返すか。</summary>
    public bool RepeatHeaderOnPageBreak { get; set; } = true;

    /// <summary>フォント（空 = 既定）。</summary>
    public string? FontFamily { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public static string NewId() => "l_" + Guid.NewGuid().ToString("N")[..10];
}

/// <summary>帳票レイアウトの領域（テキスト + 差し込みフィールド）。</summary>
public sealed class ReportLayoutRegion
{
    /// <summary>固定テキスト。</summary>
    public string? Text { get; set; }

    /// <summary>差し込む項目（レポート結果の列名または集計ラベル）。</summary>
    public List<string> Fields { get; set; } = new();

    public double HeightMm { get; set; } = 20;

    /// <summary>left / center / right。</summary>
    public string Align { get; set; } = "left";
}

/// <summary>layouts.json のルート要素。</summary>
public sealed class ReportLayoutDocument
{
    public int Version { get; set; } = 1;

    public List<ReportLayoutDefinition> Layouts { get; set; } = new();
}

/// <summary>
/// 帳票・レポートのレンダラ抽象（将来 SVF 風レンダラを追加できるようにする）。
/// Phase 1–2 では <see cref="ReportExportService"/> が唯一の実装。
/// </summary>
public interface IReportRenderer
{
    /// <summary>レンダラ ID（"document" / "svf" など）。</summary>
    string Id { get; }

    /// <summary>表示名。</summary>
    string DisplayName { get; }

    /// <summary>出力する（作成したファイルのパスを返す）。</summary>
    Task<ReportExportResult> RenderAsync(
        ReportDocument document,
        ReportExportOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary><see cref="ReportExportService"/> を <see cref="IReportRenderer"/> として公開する既定実装。</summary>
public sealed class DocumentReportRenderer : IReportRenderer
{
    public string Id => "document";

    public string DisplayName => UiText.T("Report_RendererDocument");

    public Task<ReportExportResult> RenderAsync(
        ReportDocument document,
        ReportExportOptions options,
        CancellationToken cancellationToken = default) =>
        ReportExportService.ExportAsync(document, options, cancellationToken);
}
