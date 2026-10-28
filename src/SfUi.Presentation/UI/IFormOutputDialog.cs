using SfUi.Core;

namespace SfUi.Presentation;

/// <summary>帳票テンプレート出力ダイアログの抽象化（テンプレート選択・差し込み・出力）。</summary>
public interface IFormOutputDialog
{
    /// <summary>帳票出力を表示する（キャンセル時は null）。</summary>
    Task<ReportExportResult?> ShowAsync(
        ReportDocument document,
        string suggestedBaseName,
        CancellationToken cancellationToken = default);
}
