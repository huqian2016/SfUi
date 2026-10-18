namespace SfUi.Core;

/// <summary>組織一覧のキャッシュ ファイル（起動時に CLI を待たずに復元するため）。</summary>
public sealed class OrgCacheFile
{
    /// <summary>保存日時。</summary>
    public DateTimeOffset SavedAt { get; set; }

    /// <summary>前回の組織一覧。</summary>
    public List<OrgInfo> Orgs { get; set; } = new();
}

/// <summary>組織一覧のキャッシュ（orgs-cache.json）。</summary>
public sealed class OrgCacheStore
{
    private readonly string _filePath;
    private readonly AppLog _log;

    public OrgCacheStore(AppPaths paths, AppLog log)
    {
        _filePath = Path.Combine(paths.DataRoot, "orgs-cache.json");
        _log = log;
    }

    /// <summary>保存済みの組織一覧を読み込む（存在しない・破損時は空）。</summary>
    public OrgCacheFile Load() => AtomicJsonFile.Load<OrgCacheFile>(_filePath, _log);

    /// <summary>組織一覧を保存する。</summary>
    public void Save(IReadOnlyList<OrgInfo> orgs) =>
        AtomicJsonFile.Save(_filePath, new OrgCacheFile { SavedAt = DateTimeOffset.Now, Orgs = orgs.ToList() }, _log);
}
