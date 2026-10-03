namespace SfUi.App.ViewModels;

/// <summary>組織情報セクションの表示用 1 行（言語依存の表示値へ変換済み）。</summary>
public sealed class OrgInfoRowView
{
    public string Id { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    /// <summary>行に対応する Salesforce 設定ページ（リンクのクリック対応は Step 3）。</summary>
    public string? Link { get; init; }

    /// <summary>リンクが設定されているか（リンク列のボタン有効化用）。</summary>
    public bool HasLink => !string.IsNullOrEmpty(Link);

    public IReadOnlyDictionary<string, string> Cells { get; init; } = new Dictionary<string, string>();

    /// <summary>絞り込み用テキスト（列ラベル + 表示値 + 要約の小文字連結）。</summary>
    public string SearchText { get; init; } = string.Empty;

    public string this[string key] => Cells.TryGetValue(key, out var value) ? value : string.Empty;
}
