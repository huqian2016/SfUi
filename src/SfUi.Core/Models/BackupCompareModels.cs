namespace SfUi.Core;

/// <summary>レコード差分の種類。</summary>
public enum BackupDiffKind
{
    /// <summary>B にのみ存在（追加）。</summary>
    Added,

    /// <summary>A にのみ存在（削除）。</summary>
    Removed,

    /// <summary>両方に存在し値が異なる（変更）。</summary>
    Changed,
}

/// <summary>変更された 1 項目分（A の値 → B の値）。</summary>
public sealed record BackupFieldDiff(string Field, string? ValueA, string? ValueB);

/// <summary>レコード単位の差分 1 件。</summary>
public sealed record BackupRecordDiff(
    string Id,
    string Display,
    BackupDiffKind Kind,
    IReadOnlyList<BackupFieldDiff> Fields);

/// <summary>オブジェクト単位の比較結果（件数サマリ）。</summary>
public sealed record BackupCompareObjectResult(
    string Name,
    string Label,
    int CountA,
    int CountB,
    int Added,
    int Removed,
    int Changed,
    bool OnlyInA,
    bool OnlyInB,
    string? ErrorA,
    string? ErrorB)
{
    /// <summary>比較中のエラーの有無。</summary>
    public bool HasError => ErrorA is not null || ErrorB is not null;

    /// <summary>差分の有無。</summary>
    public bool HasDiff => Added > 0 || Removed > 0 || Changed > 0;

    /// <summary>一致したレコード数。</summary>
    public int Same => Math.Max(0, CountA - Removed - Changed);
}

/// <summary>比較の進捗。</summary>
public sealed record BackupCompareProgress(int Done, int Total, string ObjectName);

/// <summary>2 つのバックアップの比較結果。</summary>
public sealed record BackupCompareResult(
    string BackupIdA,
    string LabelA,
    string BackupIdB,
    string LabelB,
    DateTimeOffset FinishedAt,
    TimeSpan Duration,
    IReadOnlyList<BackupCompareObjectResult> Objects)
{
    public int Added => Objects.Sum(o => o.Added);
    public int Removed => Objects.Sum(o => o.Removed);
    public int Changed => Objects.Sum(o => o.Changed);

    /// <summary>差分のあったオブジェクト数。</summary>
    public int DiffObjectCount => Objects.Count(o => o.HasDiff);
}

/// <summary>1 オブジェクトのレコード単位差分（表示用・上限あり）。件数は全件の集計。</summary>
public sealed record BackupCompareDetail(
    int Added,
    int Removed,
    int Changed,
    IReadOnlyList<BackupRecordDiff> Rows,
    bool Truncated);
