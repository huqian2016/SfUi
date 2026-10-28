using System.Text;

namespace SfUi.Core;

/// <summary>レポートの出力形式（複数指定可）。</summary>
[Flags]
public enum ReportFormats
{
    None = 0,

    /// <summary>CSV（1 表 = 1 ファイル）。</summary>
    Csv = 1,

    /// <summary>AI に渡しやすい Markdown（YAML フロント マター + 見出し + 表）。</summary>
    Markdown = 2,

    /// <summary>絞り込み・ソート付きの単一 HTML（オフラインで開ける）。</summary>
    Html = 4,

    /// <summary>加工しやすい xlsx（1 ブック複数シート）。</summary>
    Excel = 8,

    /// <summary>PDF（インストール済みの Chromium 系ブラウザーで HTML を印刷）。</summary>
    Pdf = 16,

    /// <summary>TSV（1 ファイルに全表）。</summary>
    Tsv = 32,

    /// <summary>JSON（メタ情報 + 全表）。</summary>
    Json = 64,

    /// <summary>JSON Lines（1 行 1 レコード）。</summary>
    JsonLines = 128,

    /// <summary>XML（table / row / 項目 API 名の要素）。</summary>
    Xml = 256,

    /// <summary>YAML（設定ファイル・AI 向け）。</summary>
    Yaml = 512,

    /// <summary>PNG 画像（表を描画・ブラウザー不要）。</summary>
    Png = 1024,

    /// <summary>JPEG 画像。</summary>
    Jpeg = 2048,

    All = Csv | Markdown | Html | Excel | Pdf | Tsv | Json | JsonLines | Xml | Yaml | Png | Jpeg,
}

/// <summary>レポート内の 1 表（列ヘッダー + 行）。</summary>
public sealed class ReportTable
{
    public required string Name { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }

    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; } = Array.Empty<IReadOnlyList<string?>>();

    /// <summary>数値として集計する列のインデックス（分析・右寄せ表示に使う）。</summary>
    public IReadOnlySet<int> NumericColumns { get; init; } = new HashSet<int>();

    public int RowCount => Rows.Count;

    /// <summary>行数を上限で切り詰めた表を返す（0 以下 = 制限なし）。</summary>
    public ReportTable Limit(int maxRows) =>
        maxRows <= 0 || Rows.Count <= maxRows
            ? this
            : new ReportTable
            {
                Name = Name,
                Columns = Columns,
                Rows = Rows.Take(maxRows).ToList(),
                NumericColumns = NumericColumns,
            };
}

/// <summary>レポートの 1 セクション（本文 / 表 / 箇条書き / 項目と値）。</summary>
public sealed class ReportSection
{
    public required string Heading { get; init; }

    /// <summary>自由記述（段落）。</summary>
    public string? Text { get; init; }

    /// <summary>ラベルと値の一覧（概要・設定値など）。</summary>
    public IReadOnlyList<KeyValuePair<string, string>>? KeyValues { get; init; }

    /// <summary>表。</summary>
    public ReportTable? Table { get; init; }

    /// <summary>箇条書き（分析結果・注意書きなど）。</summary>
    public IReadOnlyList<string>? Bullets { get; init; }

    public static ReportSection FromTable(ReportTable table, string? heading = null, string? text = null) =>
        new() { Heading = heading ?? table.Name, Table = table, Text = text };

    public static ReportSection FromBullets(string heading, IEnumerable<string> bullets, string? text = null) =>
        new() { Heading = heading, Bullets = bullets.ToList(), Text = text };
}

/// <summary>
/// 出力可能なレポート 1 件。各機能はこのモデルを組み立てるだけで、
/// CSV / Markdown / HTML / Excel / PDF の書き出しは <see cref="ReportExportService"/> に任せる。
/// </summary>
public sealed class ReportDocument
{
    public required string Title { get; init; }

    /// <summary>補足（対象組織・条件など）。</summary>
    public string? Subtitle { get; init; }

    /// <summary>組織（ユーザー名 / エイリアス）。</summary>
    public string? Org { get; init; }

    /// <summary>作成者（UI 言語やアプリ名など）。</summary>
    public string? Source { get; init; }

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;

    public List<ReportSection> Sections { get; } = new();

    /// <summary>表だけを取り出す（CSV / Excel / 分析で使用）。</summary>
    public IEnumerable<ReportTable> Tables =>
        Sections.Where(section => section.Table is not null).Select(section => section.Table!);

    /// <summary>行数の上限を適用した複製を返す（元の文書は変更しない）。</summary>
    public ReportDocument WithRowLimit(int maxRows)
    {
        var copy = new ReportDocument
        {
            Title = Title,
            Subtitle = Subtitle,
            Org = Org,
            Source = Source,
            GeneratedAt = GeneratedAt,
        };
        foreach (var section in Sections)
        {
            copy.Sections.Add(section.Table is null
                ? section
                : new ReportSection
                {
                    Heading = section.Heading,
                    Text = section.Text,
                    KeyValues = section.KeyValues,
                    Bullets = section.Bullets,
                    Table = section.Table.Limit(maxRows),
                });
        }

        return copy;
    }

    /// <summary>Markdown / HTML のフロント マター用に、各表の行数と列数を返す。</summary>
    public string DescribeStructure()
    {
        var builder = new StringBuilder();
        foreach (var section in Sections)
        {
            if (section.Table is not null)
            {
                builder.Append("- ").Append(section.Heading).Append(": ")
                    .Append(section.Table.RowCount.ToString("N0")).Append(" rows x ")
                    .Append(section.Table.Columns.Count).Append(" columns\n");
            }
        }

        return builder.ToString();
    }
}

/// <summary>レポート出力の設定（出力ダイアログで指定する値）。</summary>
public sealed class ReportExportOptions
{
    public ReportFormats Formats { get; init; } = ReportFormats.All;

    /// <summary>帳票（SVF 風）レンダラが使うレイアウト（null = 通常のレポート出力）。</summary>
    public ReportLayoutDefinition? Layout { get; init; }

    /// <summary>数値の集計・上位値などの「分析」セクションを付けるか。</summary>
    public bool IncludeAnalysis { get; init; } = true;

    /// <summary>表ごとの最大行数（0 = 制限なし）。</summary>
    public int MaxRows { get; init; } = 5000;

    public required string OutputDirectory { get; init; }

    /// <summary>拡張子を除いたファイル名。</summary>
    public required string BaseName { get; init; }

    /// <summary>保存後に出力フォルダー（または最初のファイル）を開くか。</summary>
    public bool OpenAfterSave { get; init; }
}

/// <summary>レポート出力の結果。</summary>
public sealed class ReportExportResult
{
    public required IReadOnlyList<string> Files { get; init; }

    public string? PrimaryFile => Files.FirstOrDefault();
}

/// <summary>レポート出力時の想定内エラー（PDF 用ブラウザーが無い等）。</summary>
public sealed class ReportExportException(string message) : Exception(message);
