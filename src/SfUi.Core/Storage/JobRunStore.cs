using System.Globalization;

namespace SfUi.Core;

/// <summary>
/// ジョブ実行履歴（data/scheduler/runs/&lt;yyyy-MM&gt;/&lt;runId&gt;.json）の読み書き。
/// 1 実行 = 1 ファイル。起動時に全件を読み込んでメモリに保持し、追記・更新時のみ書き出す。
/// </summary>
public sealed class JobRunStore
{
    private readonly string _runsRoot;
    private readonly AppLog _log;
    private readonly object _gate = new();
    private readonly List<JobRunRecord> _runs = new();

    public JobRunStore(AppPaths paths, AppLog log)
    {
        _runsRoot = paths.SchedulerRunsRoot;
        _log = log;
        Load();
    }

    /// <summary>履歴が追加 / 更新されたときに発火する。</summary>
    public event Action<JobRunRecord>? Updated;

    private void Load()
    {
        try
        {
            if (!Directory.Exists(_runsRoot))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_runsRoot, "*.json", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var record = AtomicJsonFile.Load<JobRunRecord>(file, _log);
                    if (!string.IsNullOrWhiteSpace(record.RunId))
                    {
                        _runs.Add(record);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn($"{file} を読み込めませんでした: {ex.Message}");
                }
            }

            _runs.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
        }
        catch (Exception ex)
        {
            _log.Error("ジョブ実行履歴を読み込めませんでした", ex);
        }
    }

    /// <summary>履歴を新しい順に返す。</summary>
    public IReadOnlyList<JobRunRecord> Query(
        int max = 500,
        string? jobId = null,
        string? status = null,
        DateTimeOffset? from = null)
    {
        lock (_gate)
        {
            IEnumerable<JobRunRecord> query = _runs;
            if (!string.IsNullOrWhiteSpace(jobId))
            {
                query = query.Where(r => string.Equals(r.JobId, jobId, StringComparison.Ordinal));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => string.Equals(r.Status, status, StringComparison.Ordinal));
            }

            if (from is { } start)
            {
                query = query.Where(r => r.StartedAt >= start);
            }

            return query.Take(Math.Max(0, max)).ToList();
        }
    }

    /// <summary>指定ジョブの直近の実行を返す。</summary>
    public JobRunRecord? Latest(string jobId) =>
        Query(1, jobId).FirstOrDefault();

    /// <summary>実行中のレコードがあるか（多重起動ガードに使う）。</summary>
    public bool IsRunning(string jobId) =>
        Query(100, jobId).Any(r => r.Status is JobRunStatuses.Running or JobRunStatuses.Queued);

    /// <summary>履歴を追記する。</summary>
    public void Append(JobRunRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.RunId))
        {
            record.RunId = NewRunId(record.JobId, record.StartedAt);
        }

        lock (_gate)
        {
            _runs.Insert(0, record);
        }

        Save(record);
        Updated?.Invoke(record);
    }

    /// <summary>既存の履歴を更新する（実行中の状態遷移に使う）。</summary>
    public void Update(JobRunRecord record)
    {
        lock (_gate)
        {
            var index = _runs.FindIndex(r => string.Equals(r.RunId, record.RunId, StringComparison.Ordinal));
            if (index >= 0)
            {
                _runs[index] = record;
            }
            else
            {
                _runs.Insert(0, record);
            }
        }

        Save(record);
        Updated?.Invoke(record);
    }

    /// <summary>保持期間・件数を超えた履歴を削除する。</summary>
    public int Prune(DateTimeOffset now, int retentionDays, int maxCount)
    {
        var deleted = 0;
        lock (_gate)
        {
            _runs.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
            var cutoff = now.AddDays(-Math.Max(1, retentionDays));
            var keep = Math.Max(1, maxCount);
            var removed = new List<JobRunRecord>();

            for (var i = 0; i < _runs.Count; i++)
            {
                var record = _runs[i];
                if (i >= keep || record.StartedAt < cutoff)
                {
                    removed.Add(record);
                }
            }

            foreach (var record in removed)
            {
                _runs.Remove(record);
                DeleteFile(record);
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>実行 ID を生成する（例: 20261006-070000-j_ab12cd34ef）。</summary>
    public static string NewRunId(string jobId, DateTimeOffset startedAt) =>
        startedAt.LocalDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + jobId;

    private void Save(JobRunRecord record)
    {
        try
        {
            var path = PathFor(record);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            AtomicJsonFile.Save(path, record, _log);
        }
        catch (Exception ex)
        {
            _log.Error($"ジョブ実行履歴を保存できませんでした ({record.RunId})", ex);
        }
    }

    private void DeleteFile(JobRunRecord record)
    {
        try
        {
            var path = PathFor(record);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(path + ".bak"))
            {
                File.Delete(path + ".bak");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"古いジョブ実行履歴を削除できませんでした ({record.RunId}): {ex.Message}");
        }
    }

    private string PathFor(JobRunRecord record) =>
        Path.Combine(_runsRoot, record.StartedAt.LocalDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture), record.RunId + ".json");
}
