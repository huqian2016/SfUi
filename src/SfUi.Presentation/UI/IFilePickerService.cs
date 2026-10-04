namespace SfUi.Presentation;

/// <summary>
/// ファイル / フォルダ選択ダイアログの抽象化（Microsoft.Win32 ダイアログの置き換え）。
/// 実装は各 UI プロジェクトが持つ（Avalonia では StorageProvider を UI スレッドで処理する）。
/// filter は WPF 形式（"CSV file (*.csv)|*.csv"）で渡し、実装側で必要に応じて変換する。
/// </summary>
public interface IFilePickerService
{
    /// <summary>ファイルを開く（キャンセル時は null）。</summary>
    Task<string?> OpenFileAsync(string title, string filter, string? initialDirectory = null);

    /// <summary>名前を付けて保存（キャンセル時は null）。</summary>
    Task<string?> SaveFileAsync(string title, string suggestedFileName, string filter, string? initialDirectory = null);

    /// <summary>フォルダを選択（キャンセル時は null）。</summary>
    Task<string?> PickFolderAsync(string title, string? initialDirectory = null);
}
