using SfUi.Core;

namespace SfUi.Presentation;

/// <summary>
/// 確認ダイアログ / 入力ダイアログの抽象化（WPF の MessageBox / InputBox の置き換え）。
/// 実装は各 UI プロジェクト（WPF / Avalonia）が持つ。
/// </summary>
public interface IDialogService
{
    /// <summary>情報ダイアログ（OK のみ）。</summary>
    void Info(string message, string caption);

    /// <summary>警告ダイアログ（OK のみ）。</summary>
    void Warning(string message, string caption);

    /// <summary>確認ダイアログ（はい / いいえ。既定は「いいえ」で安全側）。</summary>
    bool Confirm(string message, string caption);

    /// <summary>危険操作の確認（OK / キャンセル。既定は「キャンセル」）。</summary>
    bool ConfirmDestructive(string message, string caption);

    /// <summary>1 行入力ダイアログ（キャンセル時は null）。</summary>
    string? Prompt(string title, string prompt, string defaultValue = "");
}
