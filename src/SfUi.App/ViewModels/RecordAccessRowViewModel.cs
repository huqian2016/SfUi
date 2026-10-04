namespace SfUi.App.ViewModels;

/// <summary>レコードアクセス グリッドの 1 行（Id / 表示値 / リンク + ユーザーごとの権限セル）。</summary>
public sealed class RecordAccessRowViewModel
{
    public RecordAccessRowViewModel(string id, string display, string linkUrl, string[] cells)
    {
        Id = id;
        Display = display;
        LinkUrl = linkUrl;
        Cells = cells;
    }

    public string Id { get; }

    /// <summary>表示項目（例: Name）の値。</summary>
    public string Display { get; }

    /// <summary>Salesforce のレコード URL（空 = リンクなし）。</summary>
    public string LinkUrl { get; }

    /// <summary>ユーザー（列）順のセル文字列（✓ / − / 空欄）。1 ユーザー = 読取・編集・削除・転送の 4 セル。</summary>
    public string[] Cells { get; }

    public bool HasLink => !string.IsNullOrEmpty(LinkUrl);
}
