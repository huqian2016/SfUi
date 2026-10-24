using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.Tests;

/// <summary>レポート出力ダイアログを開かず、常にキャンセルするテスト用実装。</summary>
public sealed class NullReportDialog : IReportExportDialog
{
    public Task<ReportExportOptions?> ShowAsync(
        ReportDocument document,
        string suggestedBaseName,
        string? initialDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ReportExportOptions?>(null);
}
