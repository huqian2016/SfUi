namespace SfUi.Core;

/// <summary>エディタ系サブタブ 1 種類分の保存状態（本文の並び + 選択位置）。</summary>
public sealed class EditorTabHostState
{
    /// <summary>タブの本文（順序どおり）。</summary>
    public List<string> Tabs { get; set; } = new();

    /// <summary>選択中タブの位置（0 始まり）。</summary>
    public int Selected { get; set; }
}

/// <summary>エディタ系サブタブ（SOQL / 匿名Apex / コマンド）の永続化ファイル。</summary>
public sealed class EditorTabsFile
{
    /// <summary>形式バージョン（将来のマイグレーション用）。</summary>
    public int Version { get; set; } = 1;

    public EditorTabHostState? Soql { get; set; }

    public EditorTabHostState? Apex { get; set; }

    public EditorTabHostState? Command { get; set; }
}

/// <summary>エディタ系サブタブの永続化（editor-tabs.json）。</summary>
public sealed class EditorTabsStore
{
    private readonly string _filePath;
    private readonly AppLog _log;

    public EditorTabsStore(AppPaths paths, AppLog log)
    {
        _filePath = Path.Combine(paths.DataRoot, "editor-tabs.json");
        _log = log;
    }

    /// <summary>保存済みの状態を読み込む（存在しない・破損時は空の状態）。</summary>
    public EditorTabsFile Load() => AtomicJsonFile.Load<EditorTabsFile>(_filePath, _log);

    /// <summary>状態を保存する。</summary>
    public void Save(EditorTabsFile state) => AtomicJsonFile.Save(_filePath, state, _log);
}
