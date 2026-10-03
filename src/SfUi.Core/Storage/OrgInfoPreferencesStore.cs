namespace SfUi.Core;

/// <summary>
/// マイ設定（カスタムタブ定義）の読み書き（data/orginfo/preferences.json・orgKey 別）。
/// 保存時に PreferencesUpdated を発火する（複数ウィンドウ間の反映用）。
/// </summary>
public sealed class OrgInfoPreferencesStore
{
    public const int CurrentSchemaVersion = 1;

    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly object _sync = new();

    /// <summary>マイ設定の更新時 (orgKey)。</summary>
    public event Action<string>? PreferencesUpdated;

    public OrgInfoPreferencesStore(AppPaths paths, AppLog log)
    {
        _paths = paths;
        _log = log;
    }

    public string FilePath => Path.Combine(_paths.DataRoot, "orginfo", "preferences.json");

    /// <summary>組織のマイ設定を読み込む（存在しない・スキーマ不一致は空）。</summary>
    public OrgInfoOrgPreferences Load(string orgKey)
    {
        lock (_sync)
        {
            var prefs = LoadCore();
            return prefs.Orgs.TryGetValue(orgKey, out var orgPrefs)
                ? Sanitize(orgPrefs)
                : new OrgInfoOrgPreferences();
        }
    }

    /// <summary>組織のマイ設定を保存し、PreferencesUpdated を発火する。</summary>
    public void Save(string orgKey, OrgInfoOrgPreferences orgPreferences)
    {
        OrgInfoOrgPreferences sanitized;
        lock (_sync)
        {
            var prefs = LoadCore();
            sanitized = Sanitize(orgPreferences);
            prefs.Orgs[orgKey] = sanitized;
            SaveCore(prefs);
        }

        _log.Info($"マイ設定を保存: {orgKey} ({sanitized.Tabs.Count} タブ)");
        PreferencesUpdated?.Invoke(orgKey);
    }

    private OrgInfoPreferences LoadCore()
    {
        var prefs = AtomicJsonFile.Load<OrgInfoPreferences>(FilePath, _log);
        if (prefs.SchemaVersion != CurrentSchemaVersion)
        {
            _log.Info($"マイ設定のスキーマが古いため作り直します: v{prefs.SchemaVersion} → v{CurrentSchemaVersion}");
            return new OrgInfoPreferences();
        }

        prefs.Orgs ??= new Dictionary<string, OrgInfoOrgPreferences>(StringComparer.OrdinalIgnoreCase);
        return prefs;
    }

    private void SaveCore(OrgInfoPreferences prefs)
    {
        prefs.SchemaVersion = CurrentSchemaVersion;
        prefs.Orgs ??= new Dictionary<string, OrgInfoOrgPreferences>(StringComparer.OrdinalIgnoreCase);
        AtomicJsonFile.Save(FilePath, prefs, _log);
    }

    /// <summary>不明な項目 ID・重複項目・Id の無いタブを除去する。</summary>
    private static OrgInfoOrgPreferences Sanitize(OrgInfoOrgPreferences orgPreferences)
    {
        var tabs = new List<OrgInfoCustomTab>();
        foreach (var tab in orgPreferences.Tabs ?? new List<OrgInfoCustomTab>())
        {
            if (string.IsNullOrWhiteSpace(tab.Id))
            {
                continue;
            }

            var items = new List<string>();
            foreach (var itemId in tab.Items ?? new List<string>())
            {
                if (OrgInfoCatalog.Find(itemId) is not null && !items.Contains(itemId, StringComparer.Ordinal))
                {
                    items.Add(itemId);
                }
            }

            tabs.Add(new OrgInfoCustomTab
            {
                Id = tab.Id,
                Name = tab.Name ?? string.Empty,
                Items = items,
            });
        }

        return new OrgInfoOrgPreferences { Tabs = tabs };
    }
}
