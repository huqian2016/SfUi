namespace SfUi.Core;

/// <summary>組織ごとのローカル管理情報（タグ・メモ）。</summary>
public sealed class OrgManageEntry
{
    public string? Tag { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>組織管理ウィンドウのローカル状態（org-manage.json）。</summary>
public sealed class OrgManageState
{
    public int SchemaVersion { get; set; } = OrgManageStateStore.CurrentSchemaVersion;

    /// <summary>組織キー（ユーザー名）→ タグ・メモ。</summary>
    public Dictionary<string, OrgManageEntry> Orgs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 組織管理ウィンドウのローカル管理情報（タグ・メモ = data\org-manage.json）。
/// 組織側には書き込まない（すべてローカル保存）。
/// </summary>
public sealed class OrgManageStateStore
{
    public const int CurrentSchemaVersion = 1;

    private readonly string _statePath;
    private readonly AppLog _log;
    private readonly object _sync = new();

    public OrgManageStateStore(AppPaths paths, AppLog log)
    {
        _statePath = Path.Combine(paths.DataRoot, "org-manage.json");
        _log = log;
    }

    public string StatePath => _statePath;

    /// <summary>保存済みのタグ・メモをすべて取得する（未保存は空）。</summary>
    public IReadOnlyDictionary<string, OrgManageEntry> GetAll()
    {
        lock (_sync)
        {
            var state = AtomicJsonFile.Load<OrgManageState>(_statePath, _log);
            return state.Orgs.ToDictionary(
                pair => pair.Key,
                pair => new OrgManageEntry { Tag = pair.Value.Tag, Note = pair.Value.Note, UpdatedAt = pair.Value.UpdatedAt },
                StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>1 組織分のタグ・メモを取得する（未保存は null）。</summary>
    public OrgManageEntry? Get(string orgKey)
    {
        lock (_sync)
        {
            var state = AtomicJsonFile.Load<OrgManageState>(_statePath, _log);
            return state.Orgs.TryGetValue(NormalizeKey(orgKey), out var entry)
                ? new OrgManageEntry { Tag = entry.Tag, Note = entry.Note, UpdatedAt = entry.UpdatedAt }
                : null;
        }
    }

    /// <summary>タグ・メモを保存する（両方空なら項目を削除する）。</summary>
    public void Set(string orgKey, string? tag, string? note)
    {
        tag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var key = NormalizeKey(orgKey);

        lock (_sync)
        {
            var state = AtomicJsonFile.Load<OrgManageState>(_statePath, _log);
            if (tag is null && note is null)
            {
                state.Orgs.Remove(key);
            }
            else
            {
                state.Orgs[key] = new OrgManageEntry { Tag = tag, Note = note, UpdatedAt = DateTimeOffset.Now };
            }

            AtomicJsonFile.Save(_statePath, state, _log);
        }
    }

    /// <summary>
    /// 辞書は JSON 読み込み時に既定の比較子で作り直されるため、キーをここで正規化する
    /// （ユーザー名は大文字小文字を区別しない・前後の空白は無視）。
    /// </summary>
    private static string NormalizeKey(string orgKey) => orgKey.Trim().ToLowerInvariant();

    /// <summary>タグ・メモを検索対象の文字列にする（VM の絞り込み用）。</summary>
    public static string BuildSearchBlob(OrgManageEntry? entry) =>
        entry is null ? string.Empty : $"{entry.Tag} {entry.Note}";
}
