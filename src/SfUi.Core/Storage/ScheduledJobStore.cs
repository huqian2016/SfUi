namespace SfUi.Core;

/// <summary>
/// スケジュール済みジョブ定義（data/scheduler/jobs.json）の読み書き。
/// 保存は <see cref="AtomicJsonFile"/> による原子的書き込み。
/// </summary>
public sealed class ScheduledJobStore
{
    private readonly string _filePath;
    private readonly AppLog _log;
    private readonly object _gate = new();
    private readonly ScheduledJobDocument _document;

    public ScheduledJobStore(AppPaths paths, AppLog log)
    {
        _filePath = paths.SchedulerJobsFile;
        _log = log;
        _document = AtomicJsonFile.Load<ScheduledJobDocument>(_filePath, log);
        _document.Jobs ??= new List<ScheduledJob>();

        // 欠損 Id の補完（手編集された jobs.json 対策）
        var repaired = false;
        foreach (var job in _document.Jobs.Where(j => string.IsNullOrWhiteSpace(j.Id)))
        {
            job.Id = ScheduledJob.NewId();
            repaired = true;
        }

        if (repaired)
        {
            Save();
        }
    }

    /// <summary>ジョブ定義が変更されたときに発火する。</summary>
    public event Action? Changed;

    /// <summary>jobs.json のフル パス。</summary>
    public string FilePath => _filePath;

    /// <summary>現在のジョブ一覧（スナップショット）。</summary>
    public IReadOnlyList<ScheduledJob> Jobs
    {
        get
        {
            lock (_gate)
            {
                return _document.Jobs.ToList();
            }
        }
    }

    /// <summary>Id で 1 件取得する。</summary>
    public ScheduledJob? Find(string id)
    {
        lock (_gate)
        {
            return _document.Jobs.FirstOrDefault(j => string.Equals(j.Id, id, StringComparison.Ordinal));
        }
    }

    /// <summary>追加または更新する（Id が空なら新しい Id を採番）。</summary>
    public ScheduledJob Upsert(ScheduledJob job)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(job.Id))
            {
                job.Id = ScheduledJob.NewId();
            }

            var index = _document.Jobs.FindIndex(j => string.Equals(j.Id, job.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                _document.Jobs[index] = job;
            }
            else
            {
                _document.Jobs.Add(job);
            }
        }

        Save();
        return job;
    }

    /// <summary>削除する（存在した場合のみ true）。</summary>
    public bool Delete(string id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _document.Jobs.RemoveAll(j => string.Equals(j.Id, id, StringComparison.Ordinal)) > 0;
        }

        if (removed)
        {
            Save();
        }

        return removed;
    }

    /// <summary>現在の内容をファイルへ書き出す。</summary>
    public void Save()
    {
        lock (_gate)
        {
            try
            {
                AtomicJsonFile.Save(_filePath, _document, _log);
            }
            catch (Exception ex)
            {
                _log.Error("スケジューラのジョブ定義を保存できませんでした", ex);
                return;
            }
        }

        Changed?.Invoke();
    }
}
