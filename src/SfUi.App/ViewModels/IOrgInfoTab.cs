namespace SfUi.App.ViewModels;

/// <summary>組織情報ウィンドウのタブ（固定セクション / オブジェクト項目 / マイ設定）を表す。</summary>
public interface IOrgInfoTab
{
    string Title { get; }

    bool HasData { get; }

    DateTimeOffset? FetchedAt { get; }

    /// <summary>AI パネルへ添付するタブデータ（未取得・対象外は null）。</summary>
    string? BuildAttachmentText(int maxChars);

    void Relocalize();

    void Dispose();
}
