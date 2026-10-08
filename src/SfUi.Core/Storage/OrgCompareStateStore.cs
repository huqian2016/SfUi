namespace SfUi.Core;

/// <summary>組織比較ウィンドウの永続状態（選択組織・カテゴリ・差分のみフラグ）。</summary>
public sealed class OrgCompareState
{
    public int SchemaVersion { get; set; } = OrgCompareStateStore.CurrentSchemaVersion;

    /// <summary>選択していた組織のユーザー名（最大 4・重複除去）。</summary>
    public List<string> OrgUsernames { get; set; } = new();

    /// <summary>最後に表示していたカテゴリ ID（不明は null にリセット）。</summary>
    public string? CategoryId { get; set; }

    /// <summary>「差分のみ表示」の状態。</summary>
    public bool DiffOnly { get; set; }

    /// <summary>「オブジェクト項目」タブで選択していたオブジェクトの API 名。</summary>
    public string? FieldsObject { get; set; }

    /// <summary>「レコード比較」タブで選択していたオブジェクトの API 名。</summary>
    public string? RecordObject { get; set; }

    /// <summary>レコード比較の照合キー項目（API 名）。</summary>
    public string? RecordKeyField { get; set; }

    /// <summary>レコード比較の比較項目（API 名）。</summary>
    public List<string> RecordFields { get; set; } = new();

    /// <summary>レコード比較の件数上限（0 = 既定値）。</summary>
    public int RecordLimit { get; set; }
}

/// <summary>組織比較の状態を data/orginfo/compare.json に保存する（原子書き込み・スキーマ検証つき）。</summary>
public sealed class OrgCompareStateStore
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>同時に比較できる組織数の上限。</summary>
    public const int MaxOrgs = 8;

    private readonly string _filePath;
    private readonly AppLog _log;
    private readonly object _sync = new();

    public OrgCompareStateStore(AppPaths paths, AppLog log)
    {
        _filePath = Path.Combine(paths.DataRoot, "orginfo", "compare.json");
        _log = log;
    }

    public string FilePath => _filePath;

    /// <summary>状態を読み込む（無い・壊れている・スキーマ不一致は既定値）。</summary>
    public OrgCompareState Load()
    {
        lock (_sync)
        {
            var state = AtomicJsonFile.Load<OrgCompareState>(_filePath, _log);
            if (state.SchemaVersion != CurrentSchemaVersion)
            {
                _log.Info("組織比較: スキーマが古いため状態を作り直します");
                state = new OrgCompareState();
            }

            Sanitize(state);
            return state;
        }
    }

    /// <summary>状態を保存する（保存前に上限・重複をサニタイズ）。</summary>
    public void Save(OrgCompareState state)
    {
        lock (_sync)
        {
            Sanitize(state);
            AtomicJsonFile.Save(_filePath, state, _log);
        }
    }

    private static void Sanitize(OrgCompareState state)
    {
        state.SchemaVersion = CurrentSchemaVersion;
        state.OrgUsernames = state.OrgUsernames?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxOrgs)
            .ToList() ?? new List<string>();

        if (string.IsNullOrWhiteSpace(state.CategoryId) ||
            (!OrgCompareCategories.IsDynamic(state.CategoryId) && OrgCompareCategories.Find(state.CategoryId) is null))
        {
            state.CategoryId = null;
        }

        state.FieldsObject = string.IsNullOrWhiteSpace(state.FieldsObject) ? null : state.FieldsObject.Trim();
        state.RecordObject = string.IsNullOrWhiteSpace(state.RecordObject) ? null : state.RecordObject.Trim();
        state.RecordKeyField = string.IsNullOrWhiteSpace(state.RecordKeyField) ? null : state.RecordKeyField.Trim();
        state.RecordFields = state.RecordFields?
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(64)
            .ToList() ?? new List<string>();
        state.RecordLimit = Math.Clamp(state.RecordLimit, 0, OrgRecordCompareService.MaxLimit);
    }
}
