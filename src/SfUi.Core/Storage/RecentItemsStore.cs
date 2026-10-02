namespace SfUi.Core;

/// <summary>「最近使った項目」（フォルダ / URL など）の共通インターフェース。</summary>
public interface IRecentItem
{
    /// <summary>一意キー（フォルダパス / URL）。</summary>
    string Key { get; set; }

    DateTimeOffset LastUsedAt { get; set; }

    int UseCount { get; set; }

    bool IsPinned { get; set; }
}

/// <summary>JSON ファイルのラッパー。</summary>
public sealed class RecentItemsFile<T>
{
    public int Version { get; set; } = 1;
    public List<T> Items { get; set; } = new();
}

/// <summary>
/// 「最近使った項目」を JSON ファイルで管理する汎用ストア。
/// ピン留め優先 → 最終使用日時の新しい順で取得でき、上限を超えた分は古い順に削除される。
/// </summary>
public class RecentItemsStore<T>
    where T : class, IRecentItem, new()
{
    private readonly string _filePath;
    private readonly AppLog _log;
    private readonly int _maxItems;
    private readonly object _gate = new();
    private readonly RecentItemsFile<T> _file;

    public event Action? Changed;

    public RecentItemsStore(string filePath, AppLog log, int maxItems = 30)
    {
        _filePath = filePath;
        _log = log;
        _maxItems = Math.Max(1, maxItems);
        _file = AtomicJsonFile.Load<RecentItemsFile<T>>(filePath, log);
    }

    /// <summary>ピン留め優先 → 最終使用日時の新しい順で返す。</summary>
    public IReadOnlyList<T> GetOrdered()
    {
        lock (_gate)
        {
            return _file.Items
                .OrderByDescending(i => i.IsPinned)
                .ThenByDescending(i => i.LastUsedAt)
                .ToList();
        }
    }

    /// <summary>使用を記録する（新規なら追加、既存なら回数を加算して日時を更新）。</summary>
    public T Touch(string key, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.Now;
        T item;
        lock (_gate)
        {
            item = _file.Items.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase))
                   ?? CreateNewLocked(key);
            item.LastUsedAt = timestamp;
            item.UseCount++;
            TrimLocked();
            SaveLocked();
        }

        Changed?.Invoke();
        return item;
    }

    /// <summary>ピン留めを設定する。</summary>
    public void SetPinned(string key, bool pinned)
    {
        lock (_gate)
        {
            var item = _file.Items.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                return;
            }

            item.IsPinned = pinned;
            SaveLocked();
        }

        Changed?.Invoke();
    }

    /// <summary>項目を削除する。</summary>
    public void Remove(string key)
    {
        lock (_gate)
        {
            if (_file.Items.RemoveAll(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                return;
            }

            SaveLocked();
        }

        Changed?.Invoke();
    }

    /// <summary>すべて削除する。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_file.Items.Count == 0)
            {
                return;
            }

            _file.Items.Clear();
            SaveLocked();
        }

        Changed?.Invoke();
    }

    private T CreateNewLocked(string key)
    {
        var item = new T { Key = key };
        _file.Items.Add(item);
        return item;
    }

    private void TrimLocked()
    {
        if (_file.Items.Count <= _maxItems)
        {
            return;
        }

        foreach (var item in _file.Items.Where(i => !i.IsPinned).OrderBy(i => i.LastUsedAt).ToList())
        {
            if (_file.Items.Count <= _maxItems)
            {
                break;
            }

            _file.Items.Remove(item);
        }
    }

    private void SaveLocked() => AtomicJsonFile.Save(_filePath, _file, _log);
}
