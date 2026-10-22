using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>
/// 補完候補用メタデータのキャッシュ（ディスク キャッシュ優先 + 裏での更新 + 変更時のみ保存）のテスト。
/// </summary>
public sealed class SuggestionCacheTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public SuggestionCacheTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
        _log = new AppLog(_paths);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    // ---- SObjectDescribeService（オブジェクト一覧） ----

    [Fact]
    public async Task ListObjectsAsync_ServesDiskCache_AndDoesNotSaveWhenUnchanged()
    {
        var cached = new[]
        {
            new DataIoObject("Account", "Account", true, true, true, true),
            new DataIoObject("Contact", "Contact", true, true, true, true),
        };
        var cache = new FakeMetadataCache { Objects = cached.ToList() };
        var fetches = 0;
        var service = new SObjectDescribeService(
            _log,
            cache,
            (_, _) =>
            {
                Interlocked.Increment(ref fetches);
                return Task.FromResult<IReadOnlyList<DataIoObject>>(cached);
            },
            (_, name, _) => Task.FromResult(new DataIoObjectDescribe(name, name, Array.Empty<DataIoField>())));

        var items = await service.ListObjectsAsync("org");

        Assert.Equal(2, items.Count);
        await service.RefreshObjectsAsync("org");

        Assert.True(fetches >= 1);
        Assert.Equal(0, cache.SetObjectsCount);
    }

    [Fact]
    public async Task ListObjectsAsync_UpdatesCacheOnlyWhenContentChanged()
    {
        var cached = new[] { new DataIoObject("Account", "Account", true, true, true, true) };
        var fresh = new[]
        {
            new DataIoObject("Account", "Account", true, true, true, true),
            new DataIoObject("Contact", "Contact", true, true, true, true),
        };
        var cache = new FakeMetadataCache { Objects = cached.ToList() };
        var service = new SObjectDescribeService(
            _log,
            cache,
            (_, _) => Task.FromResult<IReadOnlyList<DataIoObject>>(fresh),
            (_, name, _) => Task.FromResult(new DataIoObjectDescribe(name, name, Array.Empty<DataIoField>())));

        var first = await service.ListObjectsAsync("org");
        Assert.Single(first);

        await service.RefreshObjectsAsync("org");
        var second = await service.ListObjectsAsync("org");

        Assert.Equal(2, second.Count);
        Assert.Equal(1, cache.SetObjectsCount);
        Assert.Equal(2, cache.Objects!.Count);
    }

    [Fact]
    public async Task ListObjectsAsync_WithoutCache_FetchesOnceForConcurrentCallers()
    {
        var fresh = new[] { new DataIoObject("Account", "Account", true, true, true, true) };
        var cache = new FakeMetadataCache();
        var calls = 0;
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new SObjectDescribeService(
            _log,
            cache,
            async (_, _) =>
            {
                Interlocked.Increment(ref calls);
                await gate.Task.ConfigureAwait(false);
                return fresh;
            },
            (_, name, _) => Task.FromResult(new DataIoObjectDescribe(name, name, Array.Empty<DataIoField>())));

        var first = service.ListObjectsAsync("org");
        var second = service.ListObjectsAsync("org");
        gate.SetResult(true);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.Single(results[0]);
        Assert.Equal(1, cache.SetObjectsCount);
    }

    // ---- SObjectDescribeService（項目 describe） ----

    [Fact]
    public async Task DescribeAsync_ServesDiskCache_AndDoesNotSaveWhenUnchanged()
    {
        var cached = SampleDescribe("Account", "Name");
        var cache = new FakeMetadataCache();
        cache.Describes["Account"] = cached;
        var service = new SObjectDescribeService(
            _log,
            cache,
            (_, _) => Task.FromResult<IReadOnlyList<DataIoObject>>(Array.Empty<DataIoObject>()),
            (_, _, _) => Task.FromResult(SampleDescribe("Account", "Name")));

        var describe = await service.DescribeAsync("org", "Account");

        Assert.Single(describe.Fields);
        await service.RefreshDescribeAsync("org", "Account");
        Assert.Equal(0, cache.SetDescribeCount);
    }

    [Fact]
    public async Task DescribeAsync_UpdatesCacheOnlyWhenContentChanged()
    {
        var cache = new FakeMetadataCache();
        cache.Describes["Account"] = SampleDescribe("Account", "Name");
        var service = new SObjectDescribeService(
            _log,
            cache,
            (_, _) => Task.FromResult<IReadOnlyList<DataIoObject>>(Array.Empty<DataIoObject>()),
            (_, _, _) => Task.FromResult(SampleDescribe("Account", "Name", "Industry")));

        await service.DescribeAsync("org", "Account");
        await service.RefreshDescribeAsync("org", "Account");
        var updated = await service.DescribeAsync("org", "Account");

        Assert.Equal(2, updated.Fields.Count);
        Assert.Equal(1, cache.SetDescribeCount);
    }

    // ---- OrgMetadataService（Apex クラス / メタデータ型 / ラベル） ----

    [Fact]
    public async Task ListApexClassesAsync_ServesDiskCache_AndDoesNotSaveWhenUnchanged()
    {
        var cache = new FakeMetadataCache { ApexClasses = new List<string> { "AccountHelper", "OrderService" } };
        var service = CreateMetadataService(cache, () => new[] { "AccountHelper", "OrderService" });

        var names = await service.ListApexClassesAsync("org");

        Assert.Equal(2, names.Count);
        await service.RefreshApexClassesAsync("org");
        Assert.Equal(0, cache.SetApexClassesCount);
    }

    [Fact]
    public async Task ListApexClassesAsync_UpdatesCacheWhenChanged()
    {
        var cache = new FakeMetadataCache { ApexClasses = new List<string> { "AccountHelper" } };
        var service = CreateMetadataService(cache, () => new[] { "AccountHelper", "OrderService" });

        await service.ListApexClassesAsync("org");
        await service.RefreshApexClassesAsync("org");
        var names = await service.ListApexClassesAsync("org");

        Assert.Equal(2, names.Count);
        Assert.Equal(1, cache.SetApexClassesCount);
    }

    [Fact]
    public async Task ListCustomMetadataTypesAsync_ServesDiskCache()
    {
        var cache = new FakeMetadataCache { CustomMetadataTypes = new List<string> { "ACM_CustomLabel__mdt" } };
        var service = CreateMetadataService(cache, () => Array.Empty<string>());

        var names = await service.ListCustomMetadataTypesAsync("org");

        Assert.Equal("ACM_CustomLabel__mdt", Assert.Single(names));
    }

    // ---- MetadataCacheStore（永続化） ----

    [Fact]
    public void Store_RoundTripsThroughJsonFile()
    {
        var store = new MetadataCacheStore(_paths, _log);
        store.SetObjects("my org", new[] { new DataIoObject("Account", "取引先", true, true, true, true) });
        store.SetDescribe("my org", "Account", SampleDescribe("Account", "Name"));
        store.SetApexClasses("my org", new[] { "A" });
        store.SetCustomMetadataTypes("my org", new[] { "X__mdt" });
        store.SetLabels("my org", new[] { ("Label1", "ラベル1") });

        // 別インスタンス（= アプリ再起動相当）で読み直す
        var reopened = new MetadataCacheStore(_paths, _log);
        Assert.True(reopened.TryGetObjects("my org", out var objects));
        Assert.Equal("Account", objects[0].Name);
        Assert.Equal("取引先", objects[0].Label);

        Assert.True(reopened.TryGetDescribe("my org", "Account", out var describe));
        Assert.Equal("Name", describe.Fields[0].Name);

        Assert.True(reopened.TryGetApexClasses("my org", out var classes));
        Assert.Equal("A", Assert.Single(classes));

        Assert.True(reopened.TryGetCustomMetadataTypes("my org", out var types));
        Assert.Equal("X__mdt", Assert.Single(types));

        Assert.True(reopened.TryGetLabels("my org", out var labels));
        Assert.Equal("ラベル1", labels[0].Label);
    }

    [Fact]
    public void Store_TrimsOldDescribes_AndKeepsMostRecent()
    {
        var store = new MetadataCacheStore(_paths, _log);
        for (var i = 0; i < MetadataCacheStore.MaxCachedDescribes + 5; i++)
        {
            store.SetDescribe("org", "Obj" + i, SampleDescribe("Obj" + i, "Name"));
        }

        var reopened = new MetadataCacheStore(_paths, _log);
        Assert.False(reopened.TryGetDescribe("org", "Obj0", out _));
        Assert.True(reopened.TryGetDescribe("org", "Obj" + (MetadataCacheStore.MaxCachedDescribes + 4), out _));

        var file = AtomicJsonFile.Load<MetadataCacheFile>(reopened.GetCacheFilePath("org"), _log);
        Assert.Equal(MetadataCacheStore.MaxCachedDescribes, file.Describes.Count);
    }

    [Fact]
    public void Store_Remove_DeletesCacheFile()
    {
        var store = new MetadataCacheStore(_paths, _log);
        store.SetObjects("org", new[] { new DataIoObject("Account", "Account", true, true, true, true) });
        var path = store.GetCacheFilePath("org");
        Assert.True(File.Exists(path));

        store.Remove("org");

        Assert.False(File.Exists(path));
        Assert.False(store.TryGetObjects("org", out _));
    }

    [Fact]
    public void Store_IgnoresUnknownSchemaVersion()
    {
        var store = new MetadataCacheStore(_paths, _log);
        store.SetObjects("org", new[] { new DataIoObject("Account", "Account", true, true, true, true) });

        var path = store.GetCacheFilePath("org");
        var file = AtomicJsonFile.Load<MetadataCacheFile>(path, _log);
        file.SchemaVersion = 999;
        AtomicJsonFile.Save(path, file, _log);

        var reopened = new MetadataCacheStore(_paths, _log);
        Assert.False(reopened.TryGetObjects("org", out _));
    }

    // ---- 比較ヘルパー ----

    [Fact]
    public void Comparison_TreatsEqualReferenceToListsAsSame()
    {
        var left = SampleDescribe("Contact", "AccountId");
        var right = SampleDescribe("Contact", "AccountId");

        Assert.NotSame(left.Fields[0].ReferenceTo, right.Fields[0].ReferenceTo);
        Assert.True(MetadataCacheComparison.DescribesEqual(left, right));
    }

    [Fact]
    public void Comparison_DetectsLabelDifference()
    {
        var left = SampleDescribe("Account", "Name");
        var right = SampleDescribe("Account", "Name");
        right = new DataIoObjectDescribe(right.Name, right.Label + "!", right.Fields);

        Assert.False(MetadataCacheComparison.DescribesEqual(left, right));
    }

    [Fact]
    public void Comparison_DetectsReferenceChange()
    {
        var left = SampleDescribe("Contact", "AccountId", "Account");
        var right = SampleDescribe("Contact", "AccountId", "User");

        Assert.False(MetadataCacheComparison.DescribesEqual(left, right));
    }

    [Fact]
    public void Comparison_LabelsAndSequences()
    {
        Assert.True(MetadataCacheComparison.SequenceEqual(new[] { "a", "b" }, new[] { "a", "b" }));
        Assert.False(MetadataCacheComparison.SequenceEqual(new[] { "a", "b" }, new[] { "b", "a" }));
        Assert.True(MetadataCacheComparison.LabelsEqual(new[] { ("a", "x") }, new[] { ("a", "x") }));
        Assert.False(MetadataCacheComparison.LabelsEqual(new[] { ("a", "x") }, new[] { ("a", "y") }));
    }

    // ---- ヘルパー ----

    private OrgMetadataService CreateMetadataService(FakeMetadataCache cache, Func<IReadOnlyList<string>> apexClasses) =>
        new(
            _log,
            cache,
            (_, _) => Task.FromResult(apexClasses()),
            (_, _) => Task.FromResult<IReadOnlyList<(string Name, string Label)>>(Array.Empty<(string, string)>()),
            (_, _) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));

    private static DataIoObjectDescribe SampleDescribe(string objectName, params string[] fieldNames)
    {
        var fields = fieldNames
            .Select(name => new DataIoField(
                name,
                name,
                "string",
                true,
                true,
                true,
                false,
                false,
                false,
                name.EndsWith("Id", StringComparison.Ordinal) ? new[] { "Account" } : Array.Empty<string>()))
            .ToList();
        return new DataIoObjectDescribe(objectName, objectName, fields);
    }

    private sealed class FakeMetadataCache : IMetadataCache
    {
        public List<DataIoObject>? Objects { get; set; }

        public Dictionary<string, DataIoObjectDescribe> Describes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string>? ApexClasses { get; set; }

        public List<string>? CustomMetadataTypes { get; set; }

        public List<(string Name, string Label)>? Labels { get; set; }

        public int SetObjectsCount { get; private set; }

        public int SetDescribeCount { get; private set; }

        public int SetApexClassesCount { get; private set; }

        public int SetLabelsCount { get; private set; }

        public int SetCustomMetadataTypesCount { get; private set; }

        public bool TryGetObjects(string orgKey, out IReadOnlyList<DataIoObject> objects)
        {
            objects = Objects is { Count: > 0 } ? Objects.ToList() : Array.Empty<DataIoObject>();
            return Objects is { Count: > 0 };
        }

        public void SetObjects(string orgKey, IReadOnlyList<DataIoObject> objects)
        {
            SetObjectsCount++;
            Objects = objects.ToList();
        }

        public bool TryGetDescribe(string orgKey, string objectName, out DataIoObjectDescribe describe)
        {
            if (Describes.TryGetValue(objectName, out var found))
            {
                describe = found;
                return true;
            }

            describe = new DataIoObjectDescribe(objectName, objectName, Array.Empty<DataIoField>());
            return false;
        }

        public void SetDescribe(string orgKey, string objectName, DataIoObjectDescribe describe)
        {
            SetDescribeCount++;
            Describes[objectName] = describe;
        }

        public bool TryGetApexClasses(string orgKey, out IReadOnlyList<string> names)
        {
            names = ApexClasses is { Count: > 0 } ? ApexClasses.ToList() : Array.Empty<string>();
            return ApexClasses is { Count: > 0 };
        }

        public void SetApexClasses(string orgKey, IReadOnlyList<string> names)
        {
            SetApexClassesCount++;
            ApexClasses = names.ToList();
        }

        public bool TryGetCustomMetadataTypes(string orgKey, out IReadOnlyList<string> names)
        {
            names = CustomMetadataTypes is { Count: > 0 } ? CustomMetadataTypes.ToList() : Array.Empty<string>();
            return CustomMetadataTypes is { Count: > 0 };
        }

        public void SetCustomMetadataTypes(string orgKey, IReadOnlyList<string> names)
        {
            SetCustomMetadataTypesCount++;
            CustomMetadataTypes = names.ToList();
        }

        public bool TryGetLabels(string orgKey, out IReadOnlyList<(string Name, string Label)> labels)
        {
            labels = Labels is { Count: > 0 } ? Labels.ToList() : Array.Empty<(string, string)>();
            return Labels is { Count: > 0 };
        }

        public void SetLabels(string orgKey, IReadOnlyList<(string Name, string Label)> labels)
        {
            SetLabelsCount++;
            Labels = labels.ToList();
        }

        public void Remove(string orgKey)
        {
            Objects = null;
            Describes.Clear();
            ApexClasses = null;
            CustomMetadataTypes = null;
            Labels = null;
        }
    }
}
