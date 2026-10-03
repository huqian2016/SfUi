using System.Text.Json.Serialization;

namespace SfUi.Core;

/// <summary>
/// 組織情報セクションの列定義。LabelKey は UiText のキー（表示時に現在言語へ解決する）。
/// </summary>
public sealed class OrgInfoColumn
{
    public string Key { get; set; } = string.Empty;

    public string LabelKey { get; set; } = string.Empty;

    /// <summary>現在言語の列ラベル（キャッシュには保存しない）。</summary>
    [JsonIgnore]
    public string Label => UiText.T(LabelKey);

    public OrgInfoColumn()
    {
    }

    public OrgInfoColumn(string key, string labelKey)
    {
        Key = key;
        LabelKey = labelKey;
    }
}

/// <summary>
/// 組織情報セクションの 1 行。Cells は列キー → 表示値。
/// 言語に依存する値（標準/カスタム等）は OrgInfoTokens のトークンで保存し、表示時にローカライズする。
/// </summary>
public sealed class OrgInfoRow
{
    public string Id { get; set; } = string.Empty;

    /// <summary>検索結果などで使う行の要約（組織データ由来。言語非依存）。</summary>
    public string Summary { get; set; } = string.Empty;

    public Dictionary<string, string?> Cells { get; set; } = new(StringComparer.Ordinal);

    /// <summary>行に対応する Salesforce 設定ページの URL（null = リンクなし）。</summary>
    public string? Link { get; set; }

    public string? Get(string key) => Cells.TryGetValue(key, out var value) ? value : null;
}

/// <summary>組織情報の 1 セクション（キャッシュと表示の単位）。</summary>
public sealed class OrgInfoSection
{
    public string Id { get; set; } = string.Empty;

    public DateTimeOffset? FetchedAt { get; set; }

    public long DurationMs { get; set; }

    public List<OrgInfoColumn> Columns { get; set; } = new();

    public List<OrgInfoRow> Rows { get; set; } = new();

    /// <summary>取得済みか（false = 初回取得前・再取得失敗でデータなし）。</summary>
    [JsonIgnore]
    public bool HasData => FetchedAt is not null;

    public static OrgInfoSection Create(
        string id,
        IReadOnlyList<OrgInfoColumn> columns,
        IEnumerable<OrgInfoRow> rows,
        DateTimeOffset fetchedAt,
        long durationMs) => new()
        {
            Id = id,
            Columns = columns.ToList(),
            Rows = rows.ToList(),
            FetchedAt = fetchedAt,
            DurationMs = durationMs,
        };
}
