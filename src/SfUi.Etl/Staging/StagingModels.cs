namespace SfUi.Etl.Staging;

/// <summary>ステージング列の型（SQLite の型アフィニティへマップ）。</summary>
public enum StagingColumnType
{
    /// <summary>文字列（TEXT）。</summary>
    Text,

    /// <summary>整数（INTEGER。bool も 0/1 で格納）。</summary>
    Integer,

    /// <summary>実数（REAL）。</summary>
    Real,

    /// <summary>真偽値（INTEGER の 0/1）。</summary>
    Boolean,

    /// <summary>日時（TEXT。ISO 8601 で格納）。</summary>
    DateTime,
}

/// <summary>ステージング テーブルの列定義。</summary>
/// <param name="Name">列名（ターゲット項目の API 名など）。</param>
/// <param name="Type">型。</param>
public sealed record StagingColumn(string Name, StagingColumnType Type);

/// <summary>適用キューの行操作。</summary>
public static class RowOp
{
    public const string Insert = "insert";
    public const string Update = "update";
    public const string Upsert = "upsert";
    public const string Delete = "delete";
}

/// <summary>適用キューの行ステータス。</summary>
public static class QueueStatus
{
    /// <summary>未処理。</summary>
    public const string Pending = "pending";

    /// <summary>送信済み（結果待ち）。</summary>
    public const string Sent = "sent";

    /// <summary>成功。</summary>
    public const string Ok = "ok";

    /// <summary>失敗（恒久。失敗行のみ再実行の対象）。</summary>
    public const string Failed = "failed";

    /// <summary>スキップ（方針により意図的に未適用）。</summary>
    public const string Skipped = "skipped";
}

/// <summary>適用キューの 1 行（データ値 + 制御列）。</summary>
/// <param name="RowId">キュー内の行 ID（_row）。</param>
/// <param name="Values">データ列の値（列順はステージング テーブル定義順）。</param>
/// <param name="Op">操作（insert / update / upsert / delete）。</param>
/// <param name="Status">ステータス（pending / sent / ok / failed / skipped）。</param>
/// <param name="Attempts">送信試行回数。</param>
/// <param name="TargetId">適用後のターゲット Id（成功時）。</param>
/// <param name="Error">直近のエラー メッセージ。</param>
/// <param name="JournalId">対応する journal 行の Id（あれば）。</param>
public sealed record QueueRow(
    long RowId,
    object?[] Values,
    string Op,
    string Status,
    int Attempts,
    string? TargetId,
    string? Error,
    long? JournalId);

/// <summary>journal へ追記するエントリ（適用ジャーナル = 復元のための記録）。</summary>
/// <param name="StepId">ステップ Id。</param>
/// <param name="ObjectName">オブジェクト名（論理名）。</param>
/// <param name="Op">操作。</param>
/// <param name="TargetId">ターゲット Id。</param>
/// <param name="SourceKey">ソース側のキー（crosswalk と対応）。</param>
/// <param name="BeforeJson">更新/削除前の値（before-image。JSON）。</param>
/// <param name="AfterJson">適用後の値（JSON）。</param>
public sealed record JournalEntry(
    string StepId,
    string ObjectName,
    string Op,
    string? TargetId,
    string? SourceKey,
    string? BeforeJson,
    string? AfterJson);

/// <summary>journal の 1 行。</summary>
public sealed record JournalRow(
    long Id,
    string RunId,
    string StepId,
    string ObjectName,
    string Op,
    string? TargetId,
    string? SourceKey,
    string? BeforeJson,
    string? AfterJson,
    string AppliedAt,
    string? RevertedAt,
    string? RevertStatus);
