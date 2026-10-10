namespace SfUi.Core;

/// <summary>ソース エディタが扱うメタデータ種別（Phase 1: Apex クラス / トリガー / VF ページ / LWC）。</summary>
public enum SourceMemberKind
{
    ApexClass,
    ApexTrigger,
    VisualforcePage,
    LightningComponentBundle,
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

/// <summary>
/// ソース エディタのデータ サービス（一覧 / 本文取得。Phase 2 で反映・削除を追加）。
/// </summary>
public interface ISourceEditorService
{
    /// <summary>指定種別のメンバー一覧を取得する（Tooling REST）。</summary>
    Task<IReadOnlyList<SourceMemberInfo>> ListAsync(string targetOrg, SourceMemberKind kind, CancellationToken cancellationToken = default);

    /// <summary>指定メンバーのソース本文を取得する（Apex/VF = Tooling GET、LWC = LightningComponentResource クエリ）。</summary>
    Task<IReadOnlyList<SourceFileInfo>> GetSourceAsync(string targetOrg, SourceMemberInfo member, CancellationToken cancellationToken = default);
}
