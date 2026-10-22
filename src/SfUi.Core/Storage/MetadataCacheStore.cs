using System.Collections.Concurrent;

namespace SfUi.Core;

/// <summary>describe キャッシュの 1 エントリ（使用順に並べ、古いものから破棄する）。</summary>
public sealed class DescribeCacheEntry
{
    public string ObjectName { get; set; } = string.Empty;

    public DateTimeOffset SavedAt { get; set; }

    public DataIoObjectDescribe Describe { get; set; } = new(string.Empty, string.Empty, Array.Empty<DataIoField>());
}

/// <summary>カスタム ラベル キャッシュの 1 エントリ。</summary>
public sealed class LabelCacheEntry
{
    public string Name { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// 組織単位のメタデータ キャッシュ ファイル（補完候補の高速化用）。
/// 起動直後はこのファイルから即座に候補を出し、最新かどうかは裏で確認する。
/// </summary>
public sealed class MetadataCacheFile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public DateTimeOffset SavedAt { get; set; }

    /// <summary>DescribeGlobal（オブジェクト一覧）。</summary>
    public DateTimeOffset? ObjectsSavedAt { get; set; }

    public List<DataIoObject> Objects { get; set; } = new();

    /// <summary>オブジェクト単位の describe（使用順。末尾が最新）。</summary>
    public List<DescribeCacheEntry> Describes { get; set; } = new();

    public DateTimeOffset? ApexClassesSavedAt { get; set; }

    public List<string> ApexClasses { get; set; } = new();

    public DateTimeOffset? CustomMetadataTypesSavedAt { get; set; }

    public List<string> CustomMetadataTypes { get; set; } = new();

    public DateTimeOffset? LabelsSavedAt { get; set; }

    public List<LabelCacheEntry> Labels { get; set; } = new();
}

/// <summary>メタデータ キャッシュの読み書き抽象（テストで差し替え可能）。</summary>
public interface IMetadataCache
{
    bool TryGetObjects(string orgKey, out IReadOnlyList<DataIoObject> objects);

    void SetObjects(string orgKey, IReadOnlyList<DataIoObject> objects);

    bool TryGetDescribe(string orgKey, string objectName, out DataIoObjectDescribe describe);

    void SetDescribe(string orgKey, string objectName, DataIoObjectDescribe describe);

    bool TryGetApexClasses(string orgKey, out IReadOnlyList<string> names);

    void SetApexClasses(string orgKey, IReadOnlyList<string> names);

    bool TryGetCustomMetadataTypes(string orgKey, out IReadOnlyList<string> names);

    void SetCustomMetadataTypes(string orgKey, IReadOnlyList<string> names);

    bool TryGetLabels(string orgKey, out IReadOnlyList<(string Name, string Label)> labels);

    void SetLabels(string orgKey, IReadOnlyList<(string Name, string Label)> labels);

    void Remove(string orgKey);
}

/// <summary>
/// 組織単位のメタデータ キャッシュ（data/metadata-cache/&lt;orgKey&gt;.json）の読み書き。
/// 読み込みはプロセス内で 1 回だけ行い、更新時はファイル全体を原子的に書き換える。
/// </summary>
public sealed class MetadataCacheStore : IMetadataCache
{
    /// <summary>保持する describe の最大数（使用頻度の低いものから破棄）。</summary>
    public const int MaxCachedDescribes = 40;

    private readonly string _directory;
    private readonly AppLog _log;
    private readonly ConcurrentDictionary<string, MetadataCacheFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.OrdinalIgnoreCase);

    public MetadataCacheStore(AppPaths paths, AppLog log)
    {
        _directory = Path.Combine(paths.DataRoot, "metadata-cache");
        _log = log;
    }

    /// <summary>キャッシュ ファイルのフルパス。</summary>
    public string GetCacheFilePath(string orgKey) =>
        Path.Combine(_directory, OrgInfoCacheStore.SanitizeOrgKey(orgKey) + ".json");

    public bool TryGetObjects(string orgKey, out IReadOnlyList<DataIoObject> objects)
    {
        lock (GetSyncRoot(orgKey))
        {
            var file = GetFile(orgKey);
            if (file.Objects.Count == 0)
            {
                objects = Array.Empty<DataIoObject>();
                return false;
            }

            objects = file.Objects.ToList();
            return true;
        }
    }

    public void SetObjects(string orgKey, IReadOnlyList<DataIoObject> objects) =>
        Update(orgKey, file =>
        {
            file.Objects = objects.ToList();
            file.ObjectsSavedAt = DateTimeOffset.Now;
        });

    public bool TryGetDescribe(string orgKey, string objectName, out DataIoObjectDescribe describe)
    {
        lock (GetSyncRoot(orgKey))
        {
            var file = GetFile(orgKey);
            var entry = file.Describes.FirstOrDefault(
                e => string.Equals(e.ObjectName, objectName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                describe = new DataIoObjectDescribe(objectName, objectName, Array.Empty<DataIoField>());
                return false;
            }

            describe = entry.Describe;
            return true;
        }
    }

    public void SetDescribe(string orgKey, string objectName, DataIoObjectDescribe describe) =>
        Update(orgKey, file =>
        {
            file.Describes.RemoveAll(e => string.Equals(e.ObjectName, objectName, StringComparison.OrdinalIgnoreCase));
            file.Describes.Add(new DescribeCacheEntry
            {
                ObjectName = objectName,
                SavedAt = DateTimeOffset.Now,
                Describe = describe,
            });

            while (file.Describes.Count > MaxCachedDescribes)
            {
                file.Describes.RemoveAt(0);
            }
        });

    public bool TryGetApexClasses(string orgKey, out IReadOnlyList<string> names)
    {
        lock (GetSyncRoot(orgKey))
        {
            var file = GetFile(orgKey);
            names = file.ApexClasses.ToList();
            return names.Count > 0;
        }
    }

    public void SetApexClasses(string orgKey, IReadOnlyList<string> names) =>
        Update(orgKey, file =>
        {
            file.ApexClasses = names.ToList();
            file.ApexClassesSavedAt = DateTimeOffset.Now;
        });

    public bool TryGetCustomMetadataTypes(string orgKey, out IReadOnlyList<string> names)
    {
        lock (GetSyncRoot(orgKey))
        {
            var file = GetFile(orgKey);
            names = file.CustomMetadataTypes.ToList();
            return names.Count > 0;
        }
    }

    public void SetCustomMetadataTypes(string orgKey, IReadOnlyList<string> names) =>
        Update(orgKey, file =>
        {
            file.CustomMetadataTypes = names.ToList();
            file.CustomMetadataTypesSavedAt = DateTimeOffset.Now;
        });

    public bool TryGetLabels(string orgKey, out IReadOnlyList<(string Name, string Label)> labels)
    {
        lock (GetSyncRoot(orgKey))
        {
            var file = GetFile(orgKey);
            labels = file.Labels.Select(l => (l.Name, l.Label)).ToList();
            return labels.Count > 0;
        }
    }

    public void SetLabels(string orgKey, IReadOnlyList<(string Name, string Label)> labels) =>
        Update(orgKey, file =>
        {
            file.Labels = labels.Select(l => new LabelCacheEntry { Name = l.Name, Label = l.Label }).ToList();
            file.LabelsSavedAt = DateTimeOffset.Now;
        });

    /// <summary>キャッシュを削除する（組織の再読込用）。</summary>
    public void Remove(string orgKey)
    {
        lock (GetSyncRoot(orgKey))
        {
            _files.TryRemove(orgKey, out _);
            foreach (var path in new[] { GetCacheFilePath(orgKey), GetCacheFilePath(orgKey) + ".bak" })
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn($"メタデータ キャッシュの削除に失敗: {path}: {ex.Message}");
                }
            }
        }
    }

    private MetadataCacheFile GetFile(string orgKey) =>
        _files.GetOrAdd(orgKey, key =>
        {
            var file = AtomicJsonFile.Load<MetadataCacheFile>(GetCacheFilePath(key), _log);
            return file.SchemaVersion == MetadataCacheFile.CurrentSchemaVersion ? file : new MetadataCacheFile();
        });

    private void Update(string orgKey, Action<MetadataCacheFile> mutate)
    {
        lock (GetSyncRoot(orgKey))
        {
            var file = GetFile(orgKey);
            mutate(file);
            file.SavedAt = DateTimeOffset.Now;
            AtomicJsonFile.Save(GetCacheFilePath(orgKey), file, _log);
        }
    }

    private object GetSyncRoot(string orgKey) =>
        _locks.GetOrAdd(orgKey, _ => new object());
}

/// <summary>
/// 取得し直したメタデータがキャッシュと同じかどうかの判定（同じなら更新しない）。
/// <see cref="DataIoField"/> は ReferenceTo がリストのため、レコードの等値比較では不十分。
/// </summary>
public static class MetadataCacheComparison
{
    public static bool ObjectsEqual(IReadOnlyList<DataIoObject> left, IReadOnlyList<DataIoObject> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    public static bool DescribesEqual(DataIoObjectDescribe? left, DataIoObjectDescribe? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            || !string.Equals(left.Label, right.Label, StringComparison.Ordinal)
            || left.Fields.Count != right.Fields.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Fields.Count; i++)
        {
            if (!FieldsEqual(left.Fields[i], right.Fields[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool FieldsEqual(DataIoField left, DataIoField right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal)
        && string.Equals(left.Label, right.Label, StringComparison.Ordinal)
        && string.Equals(left.Type, right.Type, StringComparison.Ordinal)
        && left.Createable == right.Createable
        && left.Updateable == right.Updateable
        && left.Nillable == right.Nillable
        && left.DefaultedOnCreate == right.DefaultedOnCreate
        && left.ExternalId == right.ExternalId
        && left.Custom == right.Custom
        && string.Equals(left.RelationshipName, right.RelationshipName, StringComparison.Ordinal)
        && string.Equals(left.CalculatedFormula, right.CalculatedFormula, StringComparison.Ordinal)
        && SequenceEqual(left.ReferenceTo, right.ReferenceTo);

    public static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public static bool LabelsEqual(
        IReadOnlyList<(string Name, string Label)> left,
        IReadOnlyList<(string Name, string Label)> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Name, right[i].Name, StringComparison.Ordinal)
                || !string.Equals(left[i].Label, right[i].Label, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
