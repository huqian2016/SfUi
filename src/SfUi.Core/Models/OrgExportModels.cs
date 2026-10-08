using System.Text;

namespace SfUi.Core;

/// <summary>
/// 定義書エクスポートの 1 シート分のデータ（列ヘッダー + 行データ）。
/// xlsx（1 ブック複数シート）と CSV（1 シート 1 ファイル）の両方へ出力できる。
/// </summary>
public sealed class ExportSheet
{
    public required string Name { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }

    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; } = Array.Empty<IReadOnlyList<string?>>();

    /// <summary>CSV（RFC 4180: CRLF 改行 + 引用符エスケープ）へ変換する。</summary>
    public string ToCsv()
    {
        var builder = new StringBuilder();
        AppendCsvRow(builder, Columns);
        foreach (var row in Rows)
        {
            AppendCsvRow(builder, row);
        }

        return builder.ToString();
    }

    private static void AppendCsvRow(StringBuilder builder, IReadOnlyList<string?> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(CsvExporter.Escape(cells[i]));
        }

        builder.Append("\r\n");
    }
}

/// <summary>エクスポートする定義書の種類。</summary>
[Flags]
public enum OrgExportDocuments
{
    None = 0,
    Objects = 1,
    Fields = 2,
    Layouts = 4,
    ListViews = 8,
    Flows = 16,
    All = Objects | Fields | Layouts | ListViews | Flows,
}

/// <summary>定義書エクスポートの要求。</summary>
public sealed class OrgExportRequest
{
    /// <summary>対象オブジェクト（API 名）。フロー以外の定義書で使用する。</summary>
    public IReadOnlyList<string> ObjectApiNames { get; init; } = Array.Empty<string>();

    public OrgExportDocuments Documents { get; init; } = OrgExportDocuments.All;

    /// <summary>フローはアクティブ版のみを対象にする（既定 true）。</summary>
    public bool ActiveFlowsOnly { get; init; } = true;

    public bool Excel { get; init; } = true;

    public bool Csv { get; init; } = true;

    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>出力ファイルのベース名（空 = 既定名: 定義書_&lt;alias&gt;_&lt;日時&gt;）。</summary>
    public string BaseName { get; init; } = string.Empty;
}

/// <summary>エクスポートの進捗段階。</summary>
public enum OrgExportStage
{
    Objects,
    Fields,
    Layouts,
    ListViews,
    Flows,
    WritingFiles,
}

/// <summary>エクスポートの進捗（Current/Total は段階内の件数）。</summary>
public sealed record OrgExportProgress(OrgExportStage Stage, int Current, int Total, string? Detail = null);

/// <summary>エクスポート結果（出力ファイル・警告・件数）。</summary>
public sealed class OrgExportResult
{
    public List<string> Files { get; } = new();

    /// <summary>一部失敗の警告（処理は継続する）。</summary>
    public List<string> Warnings { get; } = new();

    public int SheetCount { get; set; }

    public int ObjectCount { get; set; }

    public int LayoutCount { get; set; }

    public int ListViewCount { get; set; }

    public int FlowCount { get; set; }
}

/// <summary>リストビュー 1 件の概要（listviews エンドポイントから）。</summary>
public sealed record ListViewSummary(string Id, string DeveloperName, string Label, bool SoqlCompatible);

/// <summary>フロー 1 件の概要（Flow 一覧クエリから。Metadata は単一行制限のため別取得）。</summary>
public sealed record FlowSummary(
    string Id,
    string? DefinitionId,
    string Label,
    string Status,
    string ProcessType,
    int Version,
    string? LastModified);
