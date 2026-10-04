using System.Text.Json.Serialization;

namespace SfUi.Core;

/// <summary>バックアップの取得エンジン。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackupEngine
{
    /// <summary>REST API（JSON 保存）。</summary>
    Rest,

    /// <summary>Bulk API 2.0（CSV 保存）。</summary>
    Bulk,
}

/// <summary>復元時の照合方式。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RestoreMatchMode
{
    /// <summary>自動（バックアップ元と対象組織が同じ = Id / 違う = キー）。</summary>
    Auto,

    /// <summary>Id で照合（同一組織向け）。</summary>
    Id,

    /// <summary>キー項目で照合（別組織向け）。</summary>
    Key,
}

/// <summary>照合で既存レコードが見つかったときの扱い。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RestoreExistingAction
{
    /// <summary>スキップ。</summary>
    Skip,

    /// <summary>上書き（Update）。</summary>
    Overwrite,
}

/// <summary>復元時の 1 レコードに対する操作。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RestoreRecordAction
{
    Skip,
    Overwrite,
    Undelete,
    Insert,
}

/// <summary>バックアップ内の 1 オブジェクト。</summary>
public sealed record BackupObjectInfo(
    string Name,
    string Label,
    int Count,
    BackupEngine Engine,
    string File,
    string? Error = null);

/// <summary>バックアップのメタデータ（metadata.json）。</summary>
public sealed record BackupMetadata(
    string Id,
    string Label,
    string Description,
    DateTimeOffset CreatedAt,
    string? OrgId,
    string OrgUsername,
    string? OrgDisplay,
    string AppVersion,
    IReadOnlyList<BackupObjectInfo> Objects)
{
    /// <summary>合計レコード数。</summary>
    public int TotalRecords => Objects.Sum(o => o.Count);
}

/// <summary>バックアップ進捗。</summary>
public sealed record BackupProgress(int Done, int Total, string ObjectName, string Phase, int Rows);

/// <summary>復元の指定（照合方式・既存一致時の扱い・オブジェクトごとのキー項目）。</summary>
public sealed record RestoreOptions(
    RestoreMatchMode MatchMode,
    RestoreExistingAction ExistingAction,
    IReadOnlyDictionary<string, string?> KeyFields)
{
    public static RestoreOptions Default { get; } = new(
        RestoreMatchMode.Auto,
        RestoreExistingAction.Skip,
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>復元進捗。</summary>
public sealed record RestoreProgress(int Done, int Total, string ObjectName, string Phase, int Processed, int Records);

/// <summary>復元結果の 1 レコード分（表示用・上限あり）。</summary>
public sealed record RestoreRowResult(int RowIndex, string ObjectName, RestoreRecordAction Action, bool Success, string? Id, string? Error);

/// <summary>復元結果のオブジェクト別集計。</summary>
public sealed record RestoreObjectResult(
    string ObjectName,
    string Label,
    int Created,
    int Updated,
    int Undeleted,
    int Skipped,
    int Failed,
    IReadOnlyList<RestoreRowResult> Rows)
{
    public int Total => Created + Updated + Undeleted + Skipped + Failed;
    public bool HasError { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>復元結果の全体サマリ。</summary>
public sealed record RestoreSummary(
    string BackupId,
    string BackupLabel,
    DateTimeOffset FinishedAt,
    TimeSpan Duration,
    IReadOnlyList<RestoreObjectResult> Objects)
{
    public int Created => Objects.Sum(o => o.Created);
    public int Updated => Objects.Sum(o => o.Updated);
    public int Undeleted => Objects.Sum(o => o.Undeleted);
    public int Skipped => Objects.Sum(o => o.Skipped);
    public int Failed => Objects.Sum(o => o.Failed);
}

/// <summary>レコード詳細ウィンドウ用のレコード一覧（文字列表示）。</summary>
public sealed record BackupRecords(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    bool Truncated)
{
    public static BackupRecords Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<IReadOnlyDictionary<string, string?>>(),
        false);
}
