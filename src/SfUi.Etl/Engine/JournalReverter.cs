using System.Diagnostics;
using SfUi.Etl.Staging;

namespace SfUi.Etl.Engine;

/// <summary>巻き戻し可能なターゲット（journal の before-image / targetId を使って元に戻す）。</summary>
public interface IEtlRevertable
{
    /// <summary>
    /// 1 バッチ分の journal エントリを巻き戻す。結果の <see cref="RowApplyResult.RowId"/> には
    /// 対応する journal エントリの Id を返すこと。
    /// </summary>
    Task<EtlBatchResult> RevertBatchAsync(EtlApplyContext context, IReadOnlyList<JournalRow> entries, CancellationToken ct);
}

/// <summary>巻き戻しのオプション。</summary>
public sealed class RevertOptions
{
    /// <summary>1 バッチの件数。</summary>
    public int BatchSize { get; init; } = 200;

    /// <summary>一時エラー時の再試行回数（バッチ単位）。</summary>
    public int MaxRetries { get; init; } = 3;
}

/// <summary>巻き戻し結果。</summary>
public sealed class RevertResult
{
    /// <summary>処理対象件数（再試行含む送信件数は Attempted）。</summary>
    public int Attempted { get; set; }

    /// <summary>巻き戻しに成功した件数。</summary>
    public int Reverted { get; set; }

    /// <summary>巻き戻しに失敗した件数（revert_status=failed として記録）。</summary>
    public int Failed { get; set; }

    public long DurationMs { get; set; }
}

/// <summary>
/// journal（dst.sqlite）を新しい順（Id 降順 = 後から適用したものから）に巻き戻す。
/// 成功したエントリは <c>revert_status = reverted</c>、恒久失敗は <c>failed</c> として記録する。
/// </summary>
public sealed class JournalReverter
{
    private readonly RunStagingStore _store;
    private readonly IEtlRevertable _target;
    private readonly string _stepId;
    private readonly string _objectName;
    private readonly RevertOptions _options;

    public JournalReverter(
        RunStagingStore store,
        IEtlRevertable target,
        string stepId,
        string objectName,
        RevertOptions? options = null)
    {
        _store = store;
        _target = target;
        _stepId = stepId;
        _objectName = objectName;
        _options = options ?? new RevertOptions();
    }

    /// <summary>進捗（バッチ完了ごとに発火）。</summary>
    public event Action<EtlProgress>? Progress;

    /// <summary>未巻き戻しの journal エントリをすべて巻き戻す。</summary>
    public async Task<RevertResult> RevertAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new RevertResult();

        var entries = _store.QueryJournal(_objectName)
            .Where(e => e.StepId == _stepId && e.RevertedAt is null)
            .OrderByDescending(e => e.Id)
            .ToList();

        if (entries.Count == 0)
        {
            result.DurationMs = stopwatch.ElapsedMilliseconds;
            return result;
        }

        var context = new EtlApplyContext { Store = _store, StepId = _stepId, ObjectName = _objectName };
        var limit = Math.Max(1, _options.BatchSize);

        for (var offset = 0; offset < entries.Count; offset += limit)
        {
            ct.ThrowIfCancellationRequested();
            var batch = entries.Skip(offset).Take(limit).ToList();
            result.Attempted += batch.Count;

            var pending = await ApplyBatchAsync(context, batch, result, ct);

            // 一時エラーの再試行（バッチ単位）
            for (var attempt = 1; pending.Count > 0 && attempt <= _options.MaxRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var retryOutcome = await _target.RevertBatchAsync(context, pending, ct);
                pending = ProcessResults(retryOutcome, pending, result, finalAttempt: attempt >= _options.MaxRetries);
            }

            foreach (var entry in pending)
            {
                _store.MarkReverted(entry.Id, "failed");
                result.Failed++;
            }

            Progress?.Invoke(new EtlProgress
            {
                Attempted = result.Attempted,
                Success = result.Reverted,
                Failed = result.Failed,
            });
        }

        result.DurationMs = stopwatch.ElapsedMilliseconds;
        return result;
    }

    private async Task<List<JournalRow>> ApplyBatchAsync(
        EtlApplyContext context,
        List<JournalRow> batch,
        RevertResult result,
        CancellationToken ct)
    {
        var batchResult = await _target.RevertBatchAsync(context, batch, ct);
        return ProcessResults(batchResult, batch, result, finalAttempt: false);
    }

    private List<JournalRow> ProcessResults(
        EtlBatchResult batchResult,
        List<JournalRow> batch,
        RevertResult result,
        bool finalAttempt)
    {
        var map = batchResult.Rows.ToDictionary(r => r.RowId);
        var pending = new List<JournalRow>();

        foreach (var entry in batch)
        {
            if (!map.TryGetValue(entry.Id, out var outcome))
            {
                outcome = new RowApplyResult(entry.Id, false, Error: "結果が返されませんでした。");
            }

            if (outcome.Success)
            {
                _store.MarkReverted(entry.Id, "reverted");
                result.Reverted++;
            }
            else if (outcome.Transient && !finalAttempt)
            {
                pending.Add(entry);
            }
            else
            {
                _store.MarkReverted(entry.Id, "failed");
                result.Failed++;
            }
        }

        return pending;
    }
}
