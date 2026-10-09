using System.Diagnostics;
using System.Globalization;
using SfUi.Etl.Staging;

namespace SfUi.Etl.Engine;

/// <summary>
/// 適用キュー（dst.sqlite）を駆動するエンジン中核。1 ステップ = 1 ターゲット オブジェクトの適用ループを担当する。
/// <list type="bullet">
/// <item>Preflight（ターゲットの接続テスト）→ Apply（バッチ ループ）</item>
/// <item>一時エラーは pending に戻して自動リトライ（<see cref="EtlApplyOptions.MaxRetries"/> 回まで）</item>
/// <item>恒久エラーは failed に記録して続行（失敗行のみ再実行の対象）</item>
/// <item>エラー率 / 連続失敗のしきい値で自動停止（残りは pending のまま → resume 可能）</item>
/// <item>バッチ毎に run_state へ checkpoint を保存</item>
/// </list>
/// </summary>
public sealed class EtlRunner
{
    private readonly RunStagingStore _store;
    private readonly IEtlTarget _target;
    private readonly string _stepId;
    private readonly string _objectName;
    private readonly EtlApplyOptions _options;
    private readonly int _crosswalkKeyIndex;

    public EtlRunner(
        RunStagingStore store,
        IEtlTarget target,
        string stepId,
        string objectName,
        EtlApplyOptions? options = null,
        int crosswalkKeyIndex = -1)
    {
        _store = store;
        _target = target;
        _stepId = stepId;
        _objectName = objectName;
        _options = options ?? new EtlApplyOptions();
        _crosswalkKeyIndex = crosswalkKeyIndex;
    }

    /// <summary>進捗（バッチ完了ごとに発火）。</summary>
    public event Action<EtlProgress>? Progress;

    /// <summary>
    /// 適用ループを実行する。<paramref name="dryRun"/> = true の場合はターゲットへ送信せず、
    /// pending 件数のレポートのみ返す（ステータスは変化しない）。
    /// </summary>
    public async Task<EtlRunResult> RunAsync(bool dryRun = false, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new EtlRunResult();

        if (dryRun)
        {
            result.StopReason = "dry-run";
            result.Pending = PendingCount();
            result.DurationMs = stopwatch.ElapsedMilliseconds;
            return result;
        }

        if (!await _target.TestAsync(ct))
        {
            result.Stopped = true;
            result.StopReason = "preflight-failed";
            result.Pending = PendingCount();
            result.DurationMs = stopwatch.ElapsedMilliseconds;
            return result;
        }

        var limit = Math.Max(1, Math.Min(_options.BatchSize, Math.Max(1, _target.MaxBatchSize)));
        var stopReason = "completed";
        var abort = false;
        var consecutiveFailures = 0;

        while (!abort)
        {
            ct.ThrowIfCancellationRequested();
            var cursor = 0L;
            var fetchedAny = false;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var batch = _store.FetchQueueRows(_objectName, new[] { QueueStatus.Pending }, limit, cursor);
                if (batch.Count == 0)
                {
                    break;
                }

                fetchedAny = true;
                cursor = batch[^1].RowId;
                result.Attempted += batch.Count;

                var context = new EtlApplyContext { Store = _store, StepId = _stepId, ObjectName = _objectName };
                var batchResult = await _target.ApplyBatchAsync(context, batch, ct);
                var outcomes = batchResult.Rows.ToDictionary(r => r.RowId);

                foreach (var row in batch)
                {
                    if (!outcomes.TryGetValue(row.RowId, out var outcome))
                    {
                        outcome = new RowApplyResult(row.RowId, false, Error: "ターゲットから結果が返されませんでした。");
                    }

                    if (outcome.Success)
                    {
                        _store.MarkQueueRow(_objectName, row.RowId, QueueStatus.Ok, outcome.TargetId, null, outcome.JournalId, incrementAttempts: true);

                        // 親ステップの Id を crosswalk に記録（子ステップの LOOKUP で解決）
                        if (_crosswalkKeyIndex >= 0 && !string.IsNullOrEmpty(outcome.TargetId))
                        {
                            var key = Convert.ToString(
                                row.Values.Length > _crosswalkKeyIndex ? row.Values[_crosswalkKeyIndex] : null,
                                CultureInfo.InvariantCulture);
                            if (!string.IsNullOrEmpty(key))
                            {
                                _store.UpsertCrosswalk(_stepId, _objectName, key, outcome.TargetId);
                            }
                        }

                        result.Success++;
                        consecutiveFailures = 0;
                    }
                    else if (outcome.Skipped)
                    {
                        _store.MarkQueueRow(_objectName, row.RowId, QueueStatus.Skipped, null, outcome.Error, null, incrementAttempts: true);
                        result.Skipped++;
                        consecutiveFailures = 0;
                    }
                    else if (outcome.Transient && row.Attempts + 1 < Math.Max(1, _options.MaxRetries))
                    {
                        _store.MarkQueueRow(_objectName, row.RowId, QueueStatus.Pending, null, outcome.Error, null, incrementAttempts: true);
                    }
                    else
                    {
                        _store.MarkQueueRow(_objectName, row.RowId, QueueStatus.Failed, null, outcome.Error ?? "不明なエラー", null, incrementAttempts: true);
                        result.Failed++;
                        consecutiveFailures++;
                    }
                }

                _store.SetState($"checkpoint:{_stepId}", $"row={cursor}");
                Progress?.Invoke(new EtlProgress
                {
                    Attempted = result.Attempted,
                    Success = result.Success,
                    Failed = result.Failed,
                    Skipped = result.Skipped,
                });

                if (_options.MaxConsecutiveFailures > 0 && consecutiveFailures >= _options.MaxConsecutiveFailures)
                {
                    stopReason = "consecutive-failures";
                    abort = true;
                    break;
                }

                if (_options.MaxErrorRate > 0 &&
                    result.Attempted >= _options.MinRowsForErrorRate &&
                    result.Failed / (double)result.Attempted > _options.MaxErrorRate)
                {
                    stopReason = "error-rate";
                    abort = true;
                    break;
                }
            }

            if (!fetchedAny)
            {
                break;
            }
        }

        result.StopReason = stopReason;
        result.Stopped = stopReason != "completed";
        result.Pending = PendingCount();
        result.DurationMs = stopwatch.ElapsedMilliseconds;
        return result;
    }

    private int PendingCount()
        => _store.CountQueueByStatus(_objectName).TryGetValue(QueueStatus.Pending, out var pending) ? pending : 0;
}
