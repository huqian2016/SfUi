using SfUi.Core;

namespace SfUi.Presentation;

/// <summary>
/// レポート出力の設定ダイアログ（出力形式・分析・行数・出力先）の抽象化。
/// 実装は各 UI プロジェクトが持つ。
/// </summary>
public interface IReportExportDialog
{
    /// <summary>出力設定を表示する（キャンセル時は null）。</summary>
    Task<ReportExportOptions?> ShowAsync(
        ReportDocument document,
        string suggestedBaseName,
        string? initialDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>レポート出力の実行結果（キャンセル / 成功 / 失敗）。</summary>
public sealed record ReportExportOutcome(bool Canceled, ReportExportResult? Result, string? Error)
{
    public static ReportExportOutcome Cancelled() => new(true, null, null);

    public static ReportExportOutcome Succeeded(ReportExportResult result) => new(false, result, null);

    public static ReportExportOutcome Failed(string error) => new(false, null, error);
}

/// <summary>
/// どの ViewModel からでも同じ手順でレポートを出力するための共通処理。
/// ダイアログ表示 → 形式の検証 → 書き出し → 出力フォルダーを開く。
/// </summary>
public static class ReportExportRunner
{
    public static async Task<ReportExportOutcome> RunAsync(
        IReportExportDialog dialog,
        ReportDocument document,
        string suggestedBaseName,
        string? initialDirectory,
        CancellationToken cancellationToken = default)
    {
        var options = await dialog.ShowAsync(document, suggestedBaseName, initialDirectory, cancellationToken)
            .ConfigureAwait(false);
        if (options is null)
        {
            return ReportExportOutcome.Cancelled();
        }

        try
        {
            var result = await ReportExportService.ExportAsync(document, options, cancellationToken).ConfigureAwait(false);
            if (options.OpenAfterSave)
            {
                OpenFolder(options.OutputDirectory);
            }

            return ReportExportOutcome.Succeeded(result);
        }
        catch (ReportExportException ex)
        {
            return ReportExportOutcome.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            return ReportExportOutcome.Failed(ex.Message);
        }
    }

    /// <summary>出力完了時のステータス文言。</summary>
    public static string DescribeSuccess(ReportExportOutcome outcome, string outputDirectory)
    {
        var count = outcome.Result?.Files.Count ?? 0;
        var folder = outcome.Result?.PrimaryFile is { } first
            ? Path.GetDirectoryName(first) ?? outputDirectory
            : outputDirectory;
        return UiText.T("Report_DoneFmt", count.ToString("N0"), folder);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"")
                {
                    UseShellExecute = true,
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", $"\"{path}\"");
            }
        }
        catch
        {
            // フォルダーを開けない場合は無視（出力自体は成功している）
        }
    }
}
