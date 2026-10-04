using System.Collections.Concurrent;
using System.Text;

namespace SfUi.Core;

/// <summary>
/// 組織情報キャッシュ（data/orginfo/&lt;orgKey&gt;.json）の読み書き。
/// セクション単位で更新し、更新時に SectionUpdated を発火する（複数ウィンドウ間の反映用）。
/// 「初回のみ自動取得・以降は手動再取得」の制御は呼び出し側（ViewModel）が行う。
/// </summary>
public sealed class OrgInfoCacheStore
{
    public const int CurrentSchemaVersion = 1;

    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>セクション更新時 (orgKey, sectionId)。</summary>
    public event Action<string, string>? SectionUpdated;

    public OrgInfoCacheStore(AppPaths paths, AppLog log)
    {
        _paths = paths;
        _log = log;
    }

    /// <summary>組織のキャッシュキー（OrgId 優先、なければユーザー名）。</summary>
    public static string GetOrgKey(OrgInfo org) =>
        !string.IsNullOrWhiteSpace(org.OrgId) ? org.OrgId! : SanitizeOrgKey(org.Username);

    /// <summary>
    /// ファイル名に使えない文字を置換する。macOS / Linux の GetInvalidFileNameChars は
    /// '/' と NUL のみのため、Windows の禁止文字も常に置換して両 OS で同じキーにする
    /// （data フォルダーを OS 間で持ち運べるようにする）。
    /// </summary>
    public static string SanitizeOrgKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "org";
        }

        var builder = new StringBuilder(value.Trim().Length);
        foreach (var c in value.Trim())
        {
            var invalid = c < ' ' || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|';
            builder.Append(invalid ? '_' : c);
        }

        return builder.Length == 0 ? "org" : builder.ToString();
    }

    public string GetCacheFilePath(string orgKey) =>
        Path.Combine(_paths.DataRoot, "orginfo", SanitizeOrgKey(orgKey) + ".json");

    /// <summary>キャッシュを読み込む（存在しない・スキーマ不一致の場合は空）。</summary>
    public OrgInfoCache Load(string orgKey)
    {
        lock (GetSyncRoot(orgKey))
        {
            return LoadCore(orgKey);
        }
    }

    /// <summary>キャッシュ全体を保存する。</summary>
    public void Save(string orgKey, OrgInfoCache cache)
    {
        lock (GetSyncRoot(orgKey))
        {
            SaveCore(orgKey, cache);
        }
    }

    /// <summary>セクションを取得する（未取得は null）。</summary>
    public OrgInfoSection? GetSection(string orgKey, string sectionId)
    {
        lock (GetSyncRoot(orgKey))
        {
            return LoadCore(orgKey).Sections.TryGetValue(sectionId, out var section) ? section : null;
        }
    }

    /// <summary>1 セクションを更新して保存し、SectionUpdated を発火する。</summary>
    public void UpsertSection(string orgKey, OrgInfoSection section)
    {
        lock (GetSyncRoot(orgKey))
        {
            var cache = LoadCore(orgKey);
            cache.Sections[section.Id] = section;
            SaveCore(orgKey, cache);
        }

        _log.Info($"組織情報セクションを保存: {orgKey} / {section.Id} ({section.Rows.Count} 行)");
        SectionUpdated?.Invoke(orgKey, section.Id);
    }

    /// <summary>組織メタ情報を記録する（キャッシュファイルの自己記述用）。</summary>
    public void UpdateOrgMetadata(string orgKey, OrgInfo org)
    {
        lock (GetSyncRoot(orgKey))
        {
            var cache = LoadCore(orgKey);
            cache.Org = new OrgInfoCacheOrg
            {
                OrgId = org.OrgId,
                Username = org.Username,
                Alias = org.Alias,
                InstanceUrl = org.InstanceUrl,
                IsSandbox = org.IsSandbox,
            };
            SaveCore(orgKey, cache);
        }
    }

    private OrgInfoCache LoadCore(string orgKey)
    {
        var cache = AtomicJsonFile.Load<OrgInfoCache>(GetCacheFilePath(orgKey), _log);
        if (cache.SchemaVersion != CurrentSchemaVersion)
        {
            _log.Info($"組織情報キャッシュのスキーマが古いため作り直します: {orgKey} (v{cache.SchemaVersion} → v{CurrentSchemaVersion})");
            return new OrgInfoCache { Org = cache.Org };
        }

        cache.Sections ??= new Dictionary<string, OrgInfoSection>(StringComparer.Ordinal);
        return cache;
    }

    private void SaveCore(string orgKey, OrgInfoCache cache)
    {
        cache.SchemaVersion = CurrentSchemaVersion;
        cache.Sections ??= new Dictionary<string, OrgInfoSection>(StringComparer.Ordinal);
        AtomicJsonFile.Save(GetCacheFilePath(orgKey), cache, _log);
    }

    private object GetSyncRoot(string orgKey) => _locks.GetOrAdd(orgKey, _ => new object());
}
