using SfUi.Etl.Staging;

namespace SfUi.Etl.Engine;

/// <summary>ターゲットへの 1 行適用結果。</summary>
/// <param name="RowId">キューの行 Id。</param>
/// <param name="Success">成功したか。</param>
/// <param name="TargetId">適用後の Id（成功時）。</param>
/// <param name="Error">エラー メッセージ。</param>
/// <param name="Transient">一時エラー（自動リトライ対象）か。</param>
/// <param name="Skipped">方針によりスキップしたか。</param>
/// <param name="JournalId">journal へ記録済みの場合の Id。</param>
public sealed record RowApplyResult(
    long RowId,
    bool Success,
    string? TargetId = null,
    string? Error = null,
    bool Transient = false,
    bool Skipped = false,
    long? JournalId = null);

/// <summary>1 バッチ分の適用結果。</summary>
public sealed record EtlBatchResult(IReadOnlyList<RowApplyResult> Rows);

/// <summary>ターゲット ライターへ渡す適用コンテキスト（journal / crosswalk 記録用にストアを公開）。</summary>
public sealed class EtlApplyContext
{
    public required RunStagingStore Store { get; init; }

    public required string StepId { get; init; }

    public required string ObjectName { get; init; }
}

/// <summary>適用先（Salesforce / DB / ファイル出力など）の抽象。実装は before-image 取得と journal 記録を担う。</summary>
public interface IEtlTarget
{
    /// <summary>ターゲット名（表示用）。</summary>
    string Name { get; }

    /// <summary>1 バッチの最大行数。</summary>
    int MaxBatchSize { get; }

    /// <summary>事前チェック（接続テスト等）。false で実行をブロックする。</summary>
    Task<bool> TestAsync(CancellationToken ct);

    /// <summary>1 バッチを適用する。行ごとの結果を返すこと。</summary>
    Task<EtlBatchResult> ApplyBatchAsync(EtlApplyContext context, IReadOnlyList<QueueRow> rows, CancellationToken ct);
}

/// <summary>適用ループのオプション（安全装置のしきい値）。</summary>
public sealed class EtlApplyOptions
{
    /// <summary>1 バッチの行数（ターゲットの <see cref="IEtlTarget.MaxBatchSize"/> が上限）。</summary>
    public int BatchSize { get; init; } = 200;

    /// <summary>一時エラーの最大試行回数（初回送信を含む合計）。この回数を超えると failed になる。</summary>
    public int MaxRetries { get; init; } = 5;

    /// <summary>累積エラー率のしきい値。超えると自動停止（既定 5%）。</summary>
    public double MaxErrorRate { get; init; } = 0.05;

    /// <summary>エラー率判定を始める最小試行数（少数データでの過剰停止を防ぐ）。</summary>
    public int MinRowsForErrorRate { get; init; } = 100;

    /// <summary>連続恒久失敗のしきい値。超えると自動停止（0 = 無効）。</summary>
    public int MaxConsecutiveFailures { get; init; } = 50;
}

/// <summary>進捗イベント。</summary>
public sealed class EtlProgress
{
    public int Attempted { get; init; }

    public int Success { get; init; }

    public int Failed { get; init; }

    public int Skipped { get; init; }
}

/// <summary>実行結果。</summary>
public sealed class EtlRunResult
{
    /// <summary>送信を試みた行数（リトライ含む）。</summary>
    public int Attempted { get; set; }

    /// <summary>成功行数。</summary>
    public int Success { get; set; }

    /// <summary>失敗行数（恒久）。</summary>
    public int Failed { get; set; }

    /// <summary>スキップ行数。</summary>
    public int Skipped { get; set; }

    /// <summary>未処理のまま残った pending 行数。</summary>
    public int Pending { get; set; }

    /// <summary>しきい値などで途中停止したか。</summary>
    public bool Stopped { get; set; }

    /// <summary>終了理由（completed / dry-run / preflight-failed / error-rate / consecutive-failures）。</summary>
    public string StopReason { get; set; } = "completed";

    /// <summary>所要時間（ミリ秒）。</summary>
    public long DurationMs { get; set; }
}
