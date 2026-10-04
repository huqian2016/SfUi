namespace SfUi.Core;

/// <summary>バックアップタブの選択状態（backup-state.json）。</summary>
public sealed class BackupState
{
    public int SchemaVersion { get; set; } = BackupStateStore.CurrentSchemaVersion;

    /// <summary>組織キー（ユーザー名）→ 選択状態。</summary>
    public Dictionary<string, BackupOrgSelection> Orgs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>1 組織分の選択状態。</summary>
public sealed class BackupOrgSelection
{
    public List<string> SelectedObjects { get; set; } = new();

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>オブジェクト件数のキャッシュ（backups/counts.json）。</summary>
public sealed class BackupCountsState
{
    public int SchemaVersion { get; set; } = BackupStateStore.CurrentSchemaVersion;

    /// <summary>組織キー（ユーザー名）→ 件数。</summary>
    public Dictionary<string, BackupCountsOrg> Orgs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>1 組織分の件数キャッシュ。</summary>
public sealed class BackupCountsOrg
{
    public DateTimeOffset? FetchedAt { get; set; }

    public Dictionary<string, int> Counts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// バックアップ関連の永続状態（選択オブジェクト = data\backup-state.json / 件数キャッシュ = data\backups\counts.json）。
/// </summary>
public sealed class BackupStateStore
{
    public const int CurrentSchemaVersion = 1;

    private readonly string _statePath;
    private readonly string _countsPath;
    private readonly AppLog _log;
    private readonly object _sync = new();

    public BackupStateStore(AppPaths paths, AppLog log)
    {
        _statePath = Path.Combine(paths.DataRoot, "backup-state.json");
        _countsPath = Path.Combine(paths.DataRoot, "backups", "counts.json");
        _log = log;
    }

    public string StatePath => _statePath;

    public string CountsPath => _countsPath;

    // ---------- 選択オブジェクト ----------

    /// <summary>選択オブジェクトを取得する（未保存は空）。</summary>
    public IReadOnlyList<string> GetSelectedObjects(string orgKey)
    {
        lock (_sync)
        {
            var state = LoadState();
            return state.Orgs.TryGetValue(orgKey, out var org)
                ? org.SelectedObjects.ToList()
                : Array.Empty<string>();
        }
    }

    /// <summary>選択オブジェクトを保存する。</summary>
    public void SetSelectedObjects(string orgKey, IEnumerable<string> objects)
    {
        lock (_sync)
        {
            var state = LoadState();
            state.Orgs[orgKey] = new BackupOrgSelection
            {
                SelectedObjects = objects
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                UpdatedAt = DateTimeOffset.Now,
            };
            AtomicJsonFile.Save(_statePath, state, _log);
        }
    }

    // ---------- 件数キャッシュ ----------

    /// <summary>件数キャッシュを取得する（未取得は空）。</summary>
    public IReadOnlyDictionary<string, int> GetCachedCounts(string orgKey)
    {
        lock (_sync)
        {
            var state = LoadCounts();
            return state.Orgs.TryGetValue(orgKey, out var org)
                ? new Dictionary<string, int>(org.Counts, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>件数をマージ保存する（fetched が true のとき FetchedAt を更新）。</summary>
    public void UpdateCounts(string orgKey, IReadOnlyDictionary<string, int> counts, bool fetched)
    {
        if (counts.Count == 0 && !fetched)
        {
            return;
        }

        lock (_sync)
        {
            var state = LoadCounts();
            if (!state.Orgs.TryGetValue(orgKey, out var org))
            {
                org = new BackupCountsOrg();
                state.Orgs[orgKey] = org;
            }

            foreach (var pair in counts)
            {
                org.Counts[pair.Key] = pair.Value;
            }

            if (fetched)
            {
                org.FetchedAt = DateTimeOffset.Now;
            }

            AtomicJsonFile.Save(_countsPath, state, _log);
        }
    }

    /// <summary>件数キャッシュを破棄する（再取得用）。</summary>
    public void ClearCounts(string orgKey)
    {
        lock (_sync)
        {
            var state = LoadCounts();
            state.Orgs.Remove(orgKey);
            AtomicJsonFile.Save(_countsPath, state, _log);
        }
    }

    // ---------- 内部 ----------

    private BackupState LoadState()
    {
        var state = AtomicJsonFile.Load<BackupState>(_statePath, _log);
        if (state.SchemaVersion != CurrentSchemaVersion)
        {
            _log.Info("バックアップ: 状態スキーマが古いため作り直します");
            state = new BackupState();
        }

        return state;
    }

    private BackupCountsState LoadCounts()
    {
        var state = AtomicJsonFile.Load<BackupCountsState>(_countsPath, _log);
        if (state.SchemaVersion != CurrentSchemaVersion)
        {
            state = new BackupCountsState();
        }

        return state;
    }
}
