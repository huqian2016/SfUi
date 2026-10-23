namespace SfUi.Core;

/// <summary>スクラッチ組織の作成履歴（data/scratch/history.json）を管理する。</summary>
public sealed class ScratchRunHistoryStore
{
    public const int MaxRuns = 100;

    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public ScratchRunHistoryStore(AppPaths paths, AppLog log)
    {
        _paths = paths;
        _log = log;
    }

    public string HistoryFile => Path.Combine(_paths.DataRoot, "scratch", "history.json");

    /// <summary>新しい順に読み込む。</summary>
    public IReadOnlyList<ScratchRunRecord> Load()
    {
        var file = AtomicJsonFile.Load<ScratchRunHistoryFile>(HistoryFile, _log);
        return file.Runs
            .OrderByDescending(run => run.StartedAt)
            .Take(MaxRuns)
            .ToList();
    }

    /// <summary>1 件追加する（上限を超えた分は古いものから捨てる）。</summary>
    public void Add(ScratchRunRecord record)
    {
        var file = AtomicJsonFile.Load<ScratchRunHistoryFile>(HistoryFile, _log);
        file.Runs.Add(record);
        file.Runs = file.Runs
            .OrderByDescending(run => run.StartedAt)
            .Take(MaxRuns)
            .ToList();
        AtomicJsonFile.Save(HistoryFile, file, _log);
    }

    public void Clear() => AtomicJsonFile.Save(HistoryFile, new ScratchRunHistoryFile(), _log);
}
