namespace SfUi.Core;

/// <summary>ソース エディタが扱うメタデータ種別（P1-P3: Apex クラス / トリガー / VF ページ / LWC。P4: Flow = グラフ表示のみ）。</summary>
public enum SourceMemberKind
{
    ApexClass,
    ApexTrigger,
    VisualforcePage,
    LightningComponentBundle,

    /// <summary>フロー（P4。一覧は FlowDefinitionView、本文の代わりに読み取り専用グラフを表示する）。</summary>
    Flow,
}

/// <summary>一覧に表示する 1 コンポーネント。</summary>
public sealed record SourceMemberInfo(
    SourceMemberKind Kind,
    string Name,
    string Id,
    DateTime? LastModifiedDate = null,
    int? SizeHint = null)
{
    /// <summary>TreeView などの表示名（ToString を上書きして UIA 名にも使う）。</summary>
    public override string ToString() => Name;

    /// <summary>UIA 名（TreeViewItem の AutomationProperties.Name バインド用）。</summary>
    public string DisplayNodeName => Name;
}

/// <summary>取得した 1 ファイル分のソース（LWC バンドルは複数ファイル）。</summary>
/// <param name="FileName">表示名（例: helper.js）。</param>
/// <param name="LanguageId">シンタックス ハイライト定義名（C# / JavaScript / HTML / CSS / XML。空 = なし）。</param>
/// <param name="Text">本文。</param>
public sealed record SourceFileInfo(string FileName, string LanguageId, string Text);

/// <summary>ローカル ドラフト（baseline = 組織と一致した内容 / working = 編集中の内容）。</summary>
public sealed record SourceDraft(
    IReadOnlyList<SourceFileInfo> Baseline,
    IReadOnlyList<SourceFileInfo> Working);

/// <summary>ローカル履歴の 1 版（反映成功時のスナップショット。Phase 7）。</summary>
public sealed record SourceHistoryEntry(DateTime DeployedAt, IReadOnlyList<SourceFileInfo> Files)
{
    /// <summary>合計文字数（一覧のサイズ表示用）。</summary>
    public int TotalChars => Files.Sum(f => f.Text.Length);
}

/// <summary>反映エラー（Apex のコンパイル エラーは行・列付きで返る）。</summary>
public sealed record SourceDeployError(string FileName, int Line, int Column, string Problem)
{
    /// <summary>表示用（例: classes/X.cls (line 3): Unexpected token ';'.）。</summary>
    public override string ToString()
        => (Line > 0 ? FileName + " (line " + Line + ")" : FileName) + ": " + Problem;
}

/// <summary>反映（deploy）または検証（dry-run）の結果。</summary>
public sealed record SourceDeployResult(bool Success, bool DryRun, string Message, IReadOnlyList<SourceDeployError> Errors)
{
    public static SourceDeployResult Ok(bool dryRun, string message)
        => new(true, dryRun, message, Array.Empty<SourceDeployError>());

    public static SourceDeployResult Fail(string message, IReadOnlyList<SourceDeployError>? errors = null)
        => new(false, false, message, errors ?? Array.Empty<SourceDeployError>());
}

/// <summary>
/// ソース エディタのデータ サービス（一覧 / 本文取得 / 反映 / 削除 / ドラフト）。
/// </summary>
public interface ISourceEditorService
{
    /// <summary>指定種別のメンバー一覧を取得する（Tooling REST）。</summary>
    Task<IReadOnlyList<SourceMemberInfo>> ListAsync(string targetOrg, SourceMemberKind kind, CancellationToken cancellationToken = default);

    /// <summary>指定メンバーのソース本文を取得する（Apex/VF = Tooling GET、LWC = LightningComponentResource クエリ）。</summary>
    Task<IReadOnlyList<SourceFileInfo>> GetSourceAsync(string targetOrg, SourceMemberInfo member, CancellationToken cancellationToken = default);

    /// <summary>フロー一覧を取得する（FlowDefinitionView = 標準 REST オブジェクト）。</summary>
    Task<IReadOnlyList<SourceMemberInfo>> ListFlowsAsync(string targetOrg, CancellationToken cancellationToken = default);

    /// <summary>フロー 1 件の読み取り専用グラフを取得する（Tooling の Flow.Metadata。Active 版→無ければ最新版）。</summary>
    Task<FlowGraph> GetFlowGraphAsync(string targetOrg, SourceMemberInfo member, CancellationToken cancellationToken = default);

    /// <summary>ソースを組織へ反映する（sf deploy。初回はメタデータを retrieve して属性を保つ）。dryRun = 検証のみ。</summary>
    Task<SourceDeployResult> DeployAsync(string targetOrg, SourceMemberInfo member, IReadOnlyList<SourceFileInfo> files, bool dryRun, CancellationToken cancellationToken = default);

    /// <summary>メンバーを組織から削除する（Apex/VF = Tooling DELETE、LWC = sf delete source）。</summary>
    Task<SourceDeployResult> DeleteAsync(string targetOrg, SourceMemberInfo member, CancellationToken cancellationToken = default);

    /// <summary>ローカル ドラフトを読み込む（無ければ null）。</summary>
    Task<SourceDraft?> LoadDraftAsync(string orgKey, SourceMemberInfo member, CancellationToken cancellationToken = default);

    /// <summary>ローカル ドラフトを保存する。</summary>
    Task SaveDraftAsync(string orgKey, SourceMemberInfo member, IReadOnlyList<SourceFileInfo> baseline, IReadOnlyList<SourceFileInfo> working, CancellationToken cancellationToken = default);

    /// <summary>ローカル ドラフトを削除する。</summary>
    Task ClearDraftAsync(string orgKey, SourceMemberInfo member, CancellationToken cancellationToken = default);

    /// <summary>ローカル履歴（反映成功時のスナップショット）を新しい順で取得する（最大 20 版）。</summary>
    Task<IReadOnlyList<SourceHistoryEntry>> ListHistoryAsync(string orgKey, SourceMemberInfo member, CancellationToken cancellationToken = default);

    /// <summary>ローカル履歴へ 1 版を追加する（上限を超えた古い版は破棄）。</summary>
    Task SaveHistoryAsync(string orgKey, SourceMemberInfo member, IReadOnlyList<SourceFileInfo> files, CancellationToken cancellationToken = default);

    /// <summary>ローカル履歴を消去する。</summary>
    Task ClearHistoryAsync(string orgKey, SourceMemberInfo member, CancellationToken cancellationToken = default);
}
