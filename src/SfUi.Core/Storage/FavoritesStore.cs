namespace SfUi.Core;

/// <summary>お気に入り（SOQL / 匿名Apex / コマンド / API / URL / フォルダ）。</summary>
public sealed class FavoriteItem
{
    public string Id { get; set; } = "";

    /// <summary>種別（HistoryTypes と同じ値。url / folder も使用）。</summary>
    public string Type { get; set; } = "";

    /// <summary>表示名。</summary>
    public string Label { get; set; } = "";

    /// <summary>本体（SOQL 文 / Apex コード / コマンド引数 / URL / フォルダパス）。</summary>
    public string Payload { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>クイックパネル等の表示順（小さいほど先頭）。</summary>
    public int SortOrder { get; set; }
}

/// <summary>favorites.json のラッパー。</summary>
public sealed class FavoritesFile
{
    public int Version { get; set; } = 1;
    public List<FavoriteItem> Items { get; set; } = new();
}

/// <summary>お気に入りの読み書き（favorites.json）。</summary>
public sealed class FavoritesStore
{
    private readonly string _filePath;
    private readonly AppLog _log;
    private readonly object _gate = new();
    private readonly FavoritesFile _file;

    public event Action? Changed;

    public FavoritesStore(AppPaths paths, AppLog log)
    {
        _filePath = Path.Combine(paths.DataRoot, "favorites.json");
        _log = log;
        _file = AtomicJsonFile.Load<FavoritesFile>(_filePath, log);
    }

    /// <summary>すべてのお気に入りを表示順で返す。</summary>
    public IReadOnlyList<FavoriteItem> GetAll()
    {
        lock (_gate)
        {
            return _file.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedAt).ToList();
        }
    }

    /// <summary>指定種別のお気に入りを表示順で返す。</summary>
    public IReadOnlyList<FavoriteItem> GetByType(string type)
    {
        lock (_gate)
        {
            return _file.Items
                .Where(i => string.Equals(i.Type, type, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.CreatedAt)
                .ToList();
        }
    }

    public FavoriteItem? Find(string id)
    {
        lock (_gate)
        {
            return _file.Items.FirstOrDefault(i => i.Id == id);
        }
    }

    /// <summary>追加する（Id / CreatedAt / SortOrder は自動設定）。</summary>
    public FavoriteItem Add(string type, string label, string payload)
    {
        FavoriteItem item;
        lock (_gate)
        {
            item = new FavoriteItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = type,
                Label = label,
                Payload = payload,
                CreatedAt = DateTimeOffset.Now,
                SortOrder = _file.Items.Count == 0 ? 1 : _file.Items.Max(i => i.SortOrder) + 1,
            };
            _file.Items.Add(item);
            SaveLocked();
        }

        Changed?.Invoke();
        return item;
    }

    /// <summary>ラベル / 本体を更新する。</summary>
    public void Update(string id, string? label = null, string? payload = null)
    {
        lock (_gate)
        {
            var item = _file.Items.FirstOrDefault(i => i.Id == id);
            if (item is null)
            {
                return;
            }

            if (label is not null)
            {
                item.Label = label;
            }

            if (payload is not null)
            {
                item.Payload = payload;
            }

            SaveLocked();
        }

        Changed?.Invoke();
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            if (_file.Items.RemoveAll(i => i.Id == id) == 0)
            {
                return;
            }

            SaveLocked();
        }

        Changed?.Invoke();
    }

    private void SaveLocked() => AtomicJsonFile.Save(_filePath, _file, _log);
}
