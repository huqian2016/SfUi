using SfUi.Etl.Sources;
using SfUi.Etl.Staging;
using SfUi.Etl.Transforms;

namespace SfUi.Etl.Engine;

/// <summary>1 ステップ分の実行計画（入力 → マッピング → 出力）。</summary>
public sealed class EtlStepPlan
{
    /// <summary>ステップ Id（journal / checkpoint の単位）。</summary>
    public required string StepId { get; init; }

    /// <summary>オブジェクト名（ステージング テーブル名 <c>stg_&lt;name&gt;</c> と Salesforce 出力先で共用）。</summary>
    public required string ObjectName { get; init; }

    /// <summary>入力ソース。</summary>
    public required IEtlSource Source { get; init; }

    /// <summary>マッピング変換。</summary>
    public required RowMapper Mapper { get; init; }

    /// <summary>出力ターゲット。</summary>
    public required IEtlTarget Target { get; init; }

    /// <summary>巻き戻し対応ターゲット（自動ロールバック / 手動リストア用。null = 非対応）。</summary>
    public IEtlRevertable? Revertable { get; init; }
}

/// <summary>1 ステップ実行の結果。</summary>
public sealed class EtlStepRunResult
{
    /// <summary>ステージングへ投入した行数。</summary>
    public int Loaded { get; set; }

    /// <summary>適用キューへ積んだ行数。</summary>
    public int Enqueued { get; set; }

    /// <summary>適用ループの結果（dry-run の場合も設定される）。</summary>
    public EtlRunResult? Apply { get; set; }

    /// <summary>自動ロールバックの結果（実行された場合のみ）。</summary>
    public RevertResult? Rollback { get; set; }
}

/// <summary>
/// 1 ステップの実行を束ねる（Prepare: ソース → マッピング → ステージング → 適用キュー投入、
/// Apply: 適用ループ、失敗時の自動ロールバック）。
/// </summary>
public sealed class EtlStepRun
{
    private readonly RunStagingStore _store;
    private readonly EtlStepPlan _plan;
    private readonly EtlApplyOptions _applyOptions;

    public EtlStepRun(RunStagingStore store, EtlStepPlan plan, EtlApplyOptions? applyOptions = null)
    {
        _store = store;
        _plan = plan;
        _applyOptions = applyOptions ?? new EtlApplyOptions();
    }

    public EtlStepPlan Plan => _plan;

    /// <summary>適用ループの進捗イベント。</summary>
    public event Action<EtlProgress>? Progress;

    /// <summary>ソース → マッピング → ステージング投入 → 適用キュー投入。</summary>
    public (int Loaded, int Enqueued) Prepare()
    {
        _store.CreateStagingTable(_plan.ObjectName, _plan.Mapper.StagingColumns);
        var loaded = _store.InsertStagingRows(_plan.ObjectName, _plan.Mapper.MapAll(_plan.Source.ReadRows()));
        var enqueued = _store.EnqueueFromStaging(_plan.ObjectName);
        return (loaded, enqueued);
    }

    /// <summary>適用ループを実行する（dry-run はレポートのみ）。</summary>
    public async Task<EtlRunResult> ApplyAsync(bool dryRun = false, CancellationToken ct = default)
    {
        var runner = new EtlRunner(_store, _plan.Target, _plan.StepId, _plan.ObjectName, _applyOptions);
        if (Progress is not null)
        {
            runner.Progress += p => Progress(p);
        }

        return await runner.RunAsync(dryRun, ct).ConfigureAwait(false);
    }

    /// <summary>journal から巻き戻す（手動リストア / 成功後の巻き戻しにも使用）。</summary>
    public async Task<RevertResult> RollbackAsync(CancellationToken ct = default)
    {
        if (_plan.Revertable is null)
        {
            throw new InvalidOperationException("このステップのターゲットは巻き戻しに対応していません。");
        }

        return await new JournalReverter(_store, _plan.Revertable, _plan.StepId, _plan.ObjectName).RevertAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Prepare → Apply を通しで実行する。<paramref name="autoRollbackOnFailure"/> = true の場合、
    /// しきい値などで途中停止したときに journal から自動ロールバックする。
    /// </summary>
    public async Task<EtlStepRunResult> RunAsync(
        bool dryRun = false,
        bool autoRollbackOnFailure = true,
        CancellationToken ct = default)
    {
        var result = new EtlStepRunResult();
        var (loaded, enqueued) = Prepare();
        result.Loaded = loaded;
        result.Enqueued = enqueued;

        result.Apply = await ApplyAsync(dryRun, ct).ConfigureAwait(false);

        if (!dryRun && result.Apply.Stopped && autoRollbackOnFailure && _plan.Revertable is not null)
        {
            result.Rollback = await RollbackAsync(ct).ConfigureAwait(false);
        }

        return result;
    }
}
