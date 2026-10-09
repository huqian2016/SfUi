using System.Diagnostics;
using SfUi.Etl.Staging;

namespace SfUi.Etl.Engine;

/// <summary>マルチステップ ジョブの実行計画（親 → 子の順に並んだステップ）。</summary>
public sealed class EtlJobPlan
{
    /// <summary>ジョブ名（表示用）。</summary>
    public required string JobName { get; init; }

    /// <summary>ステップ（親 → 子の順。先行ステップの crosswalk が後続の LOOKUP で解決される）。</summary>
    public required IReadOnlyList<EtlStepPlan> Steps { get; init; }
}

/// <summary>マルチステップ ジョブの実行結果。</summary>
public sealed class EtlJobResult
{
    public string JobName { get; set; } = string.Empty;

    /// <summary>実行したステップ Id（実行順。途中停止の場合は停止まで）。</summary>
    public List<string> StepIds { get; } = new();

    /// <summary>ステップごとの結果。</summary>
    public List<EtlStepRunResult> Steps { get; } = new();

    /// <summary>しきい値などで途中停止したか。</summary>
    public bool Stopped { get; set; }

    /// <summary>終了理由（completed / error-rate / consecutive-failures / preflight-failed / dry-run）。</summary>
    public string StopReason { get; set; } = "completed";

    /// <summary>自動ロールバックの結果（子 → 親の順に実行）。</summary>
    public List<RevertResult> Rollbacks { get; } = new();

    /// <summary>ロールバックに失敗したステップのエラー（途中停止時も残りを継続）。</summary>
    public List<string> RollbackErrors { get; } = new();

    /// <summary>事前バックアップの結果情報（フックが実行された場合のみ）。</summary>
    public string? BackupInfo { get; set; }

    public long DurationMs { get; set; }
}

/// <summary>
/// マルチステップ ジョブの実行を束ねる。各ステップは Prepare → Apply を順に実行し、
/// しきい値などで途中停止した場合は <b>子 → 親の逆順</b>で自動ロールバックする。
/// 親ステップで <see cref="EtlStepPlan.CrosswalkKeyField"/> を設定すると適用成功時に crosswalk へ
/// (source_key → target_id) が記録され、子ステップの <c>LOOKUP</c>（<see cref="EtlCrosswalk.WireLookup"/>）で解決できる。
/// </summary>
public sealed class EtlJobRunner
{
    private readonly RunStagingStore _store;
    private readonly EtlJobPlan _plan;
    private readonly EtlApplyOptions _options;

    public EtlJobRunner(RunStagingStore store, EtlJobPlan plan, EtlApplyOptions? options = null)
    {
        _store = store;
        _plan = plan;
        _options = options ?? new EtlApplyOptions();
    }

    /// <summary>ステップごとの進捗イベント（stepId, progress）。</summary>
    public event Action<string, EtlProgress>? StepProgress;

    /// <summary>事前バックアップ フック（dry-run 以外の実行で最初のステップ前に呼ばれる）。</summary>
    public Func<CancellationToken, Task<string?>>? PreRunBackup { get; set; }

    /// <summary>全ステップを順に実行する。</summary>
    public async Task<EtlJobResult> RunAsync(
        bool dryRun = false,
        bool autoRollbackOnFailure = true,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new EtlJobResult { JobName = _plan.JobName };
        var stepRuns = new List<EtlStepRun>();

        if (!dryRun && PreRunBackup is not null)
        {
            result.BackupInfo = await PreRunBackup(ct).ConfigureAwait(false);
        }

        foreach (var stepPlan in _plan.Steps)
        {
            ct.ThrowIfCancellationRequested();

            var stepRun = new EtlStepRun(_store, stepPlan, _options);
            if (StepProgress is not null)
            {
                var stepId = stepPlan.StepId;
                stepRun.Progress += p => StepProgress(stepId, p);
            }

            stepRuns.Add(stepRun);
            var stepResult = await stepRun.RunAsync(dryRun, autoRollbackOnFailure: false, ct).ConfigureAwait(false);
            result.StepIds.Add(stepPlan.StepId);
            result.Steps.Add(stepResult);

            if (!dryRun && stepResult.Apply is { Stopped: true } apply)
            {
                result.Stopped = true;
                result.StopReason = apply.StopReason;
                break;
            }
        }

        if (!dryRun && result.Stopped && autoRollbackOnFailure)
        {
            // 子 → 親の逆順で巻き戻す（1 ステップの失敗でも残りを継続）
            for (var i = stepRuns.Count - 1; i >= 0; i--)
            {
                var stepRun = stepRuns[i];
                if (stepRun.Plan.Revertable is null)
                {
                    continue;
                }

                try
                {
                    result.Rollbacks.Add(await stepRun.RollbackAsync(ct).ConfigureAwait(false));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.RollbackErrors.Add(stepRun.Plan.StepId + ": " + ex.Message);
                }
            }
        }

        result.DurationMs = stopwatch.ElapsedMilliseconds;
        return result;
    }
}
