using SfUi.Core;

namespace SfUi.Presentation;

/// <summary>
/// 帳票（SVF 風）出力ダイアログの抽象化。レイアウトの選択・編集と出力を行う。
/// 実装は各 UI プロジェクトが持つ。
/// </summary>
public interface IReportFormDialog
{
    /// <summary>帳票出力を表示する（キャンセル時は null）。</summary>
    Task<ReportExportResult?> ShowAsync(
        ReportDocument document,
        string suggestedBaseName,
        CancellationToken cancellationToken = default);
}
