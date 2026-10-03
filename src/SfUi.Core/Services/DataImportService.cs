using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>データインポート（REST SObject Collections / Bulk API 2.0）。</summary>
public sealed class DataImportService
{
    private readonly SalesforceRestClient _rest;
    private readonly SfCliRunner _runner;
    private readonly AppLog _log;

    public DataImportService(SalesforceRestClient rest, SfCliRunner runner, AppLog log)
    {
        _rest = rest;
        _runner = runner;
        _log = log;
    }

    /// <summary>REST でインポートする（Insert / Update / Delete は composite、Upsert は逐次 PATCH）。</summary>
    public async Task<ImportRunResult> RunRestAsync(
        string targetOrg,
        string objectName,
        DataImportOperation operation,
        string? externalIdField,
        ImportPlan plan,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var version = await _rest.GetApiVersionAsync(targetOrg, cancellationToken).ConfigureAwait(false);
        var results = new List<ImportRowResult>(plan.Rows.Count);
        var processed = 0;

        foreach (var row in plan.Rows.Where(r => r.Error is not null))
        {
            results.Add(new ImportRowResult(row.RowIndex, false, null, row.Error));
        }

        progress?.Report(new ImportProgress(0, plan.Rows.Count, "rest"));

        if (operation == DataImportOperation.Upsert)
        {
            foreach (var row in plan.Rows.Where(r => r.Error is null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = $"/services/data/v{version}/sobjects/{Uri.EscapeDataString(objectName)}/" +
                           $"{Uri.EscapeDataString(externalIdField ?? string.Empty)}/{Uri.EscapeDataString(row.ExternalIdValue ?? string.Empty)}";
                try
                {
                    var body = JsonSerializer.Serialize(row.Fields);
                    var response = await _rest.SendRawAsync(targetOrg, HttpMethod.Patch, path, body, cancellationToken).ConfigureAwait(false);
                    results.Add(ImportResultMapper.FromSingleResponse(response, row.RowIndex));
                }
                catch (SalesforceApiException ex)
                {
                    results.Add(new ImportRowResult(row.RowIndex, false, null, ex.Message));
                }

                processed++;
                progress?.Report(new ImportProgress(processed, plan.Rows.Count, "rest"));
            }
        }
        else
        {
            foreach (var batch in ImportBatchPlanner.ChunkSendable(plan.Rows))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    List<ImportRowResult> batchResults;
                    switch (operation)
                    {
                        case DataImportOperation.Insert:
                            var insertBody = ImportBatchPlanner.BuildCompositeBody(batch, objectName, includeId: false);
                            var insertResponse = await _rest.SendRawAsync(
                                targetOrg, HttpMethod.Post, $"/services/data/v{version}/composite/sobjects", insertBody, cancellationToken).ConfigureAwait(false);
                            batchResults = ImportResultMapper.FromCompositeResponse(insertResponse, batch);
                            break;

                        case DataImportOperation.Update:
                            var updateBody = ImportBatchPlanner.BuildCompositeBody(batch, objectName, includeId: true);
                            var updateResponse = await _rest.SendRawAsync(
                                targetOrg, HttpMethod.Patch, $"/services/data/v{version}/composite/sobjects", updateBody, cancellationToken).ConfigureAwait(false);
                            batchResults = ImportResultMapper.FromCompositeResponse(updateResponse, batch);
                            break;

                        default:
                            var ids = string.Join(",", batch.Select(b => b.Id));
                            var deleteResponse = await _rest.SendRawAsync(
                                targetOrg, HttpMethod.Delete, $"/services/data/v{version}/composite/sobjects?ids={ids}", null, cancellationToken).ConfigureAwait(false);
                            batchResults = ImportResultMapper.FromCompositeResponse(deleteResponse, batch);
                            break;
                    }

                    results.AddRange(batchResults);
                }
                catch (SalesforceApiException ex)
                {
                    _log.Warn($"データインポート（REST）バッチ失敗: {ex.Message}");
                    foreach (var row in batch)
                    {
                        results.Add(new ImportRowResult(row.RowIndex, false, null, ex.Message));
                    }
                }

                processed += batch.Count;
                progress?.Report(new ImportProgress(processed, plan.Rows.Count, "rest"));
            }
        }

        return BuildResult(plan, results, null, stopwatch.Elapsed, null, null);
    }

    /// <summary>Bulk API 2.0 でインポートする（マッピング済み CSV を一時ファイルに書き sf data * bulk を実行）。</summary>
    public async Task<ImportRunResult> RunBulkAsync(
        string targetOrg,
        string objectName,
        DataImportOperation operation,
        string? externalIdField,
        ImportPlan plan,
        string tempCsvPath,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<ImportRowResult>(plan.Rows.Count);

        foreach (var row in plan.Rows.Where(r => r.Error is not null))
        {
            results.Add(new ImportRowResult(row.RowIndex, false, null, row.Error));
        }

        var (csv, headers) = ImportBatchPlanner.BuildBulkCsv(plan.Rows, operation, externalIdField);
        await File.WriteAllTextAsync(tempCsvPath, csv, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

        var command = operation switch
        {
            DataImportOperation.Insert => "import",
            DataImportOperation.Update => "update",
            DataImportOperation.Upsert => "upsert",
            _ => "delete",
        };

        var arguments = new List<string>
        {
            "data", command, "bulk",
            "--target-org", targetOrg,
            "--sobject", objectName,
            "--file", tempCsvPath,
            "--wait", "30",
            "--json",
        };

        if (operation == DataImportOperation.Upsert)
        {
            arguments.Add("--external-id");
            arguments.Add(externalIdField!);
        }

        progress?.Report(new ImportProgress(0, plan.Rows.Count, "bulk"));
        var run = await _runner.RunAsync(arguments, timeout: TimeSpan.FromMinutes(31), cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!run.Success)
        {
            var message = DescribeFailure(run);
            foreach (var row in plan.Rows.Where(r => r.Error is null))
            {
                results.Add(new ImportRowResult(row.RowIndex, false, null, message));
            }

            return BuildResult(plan, results, null, stopwatch.Elapsed, null, null);
        }

        var (jobId, processedCount, failedCount) = ImportResultMapper.ParseBulkJobInfo(run.StdOut);
        _log.Info($"Bulk インポート完了: {targetOrg} / {command} {objectName} / ジョブ {jobId ?? "(不明)"} / 処理 {processedCount} 失敗 {failedCount}");

        if (failedCount > 0 && jobId is not null)
        {
            var remaining = failedCount;
            var failedRows = await TryGetBulkFailuresAsync(targetOrg, jobId, plan, headers, cancellationToken).ConfigureAwait(false);
            if (failedRows.Count > 0)
            {
                var matched = failedRows.Where(f => f.RowIndex >= 0).ToList();
                var unmatched = failedRows.Where(f => f.RowIndex < 0).ToList();
                var failedSet = matched.Select(f => f.RowIndex).ToHashSet();
                var byIndex = matched.ToDictionary(f => f.RowIndex);

                foreach (var row in plan.Rows.Where(r => r.Error is null))
                {
                    results.Add(failedSet.Contains(row.RowIndex)
                        ? new ImportRowResult(row.RowIndex, false, byIndex[row.RowIndex].Id, byIndex[row.RowIndex].Error)
                        : new ImportRowResult(row.RowIndex, true, null, null));
                }

                results.AddRange(unmatched);
                remaining = Math.Max(0, failedCount - matched.Count - unmatched.Count);
            }
            else
            {
                foreach (var row in plan.Rows.Where(r => r.Error is null))
                {
                    results.Add(new ImportRowResult(row.RowIndex, true, null, null));
                }
            }

            // 詳細を特定できなかった失敗分は識別不能行として追加する
            for (var i = 0; i < remaining; i++)
            {
                results.Add(new ImportRowResult(-1, false, null, UiText.T("DataIo_Err_BulkDetailUnavailable")));
            }
        }
        else
        {
            foreach (var row in plan.Rows.Where(r => r.Error is null))
            {
                results.Add(new ImportRowResult(row.RowIndex, true, null, null));
            }
        }

        progress?.Report(new ImportProgress(plan.Rows.Count, plan.Rows.Count, "done"));

        // ジョブ統計が取れている場合は成功/失敗数を優先する（行の特定が曖昧なため）
        int? overrideSuccess = null;
        int? overrideFailed = null;
        if (processedCount > 0 || failedCount > 0)
        {
            overrideSuccess = Math.Max(0, processedCount - failedCount);
            overrideFailed = failedCount;
        }

        return BuildResult(plan, results, jobId, stopwatch.Elapsed, overrideSuccess, overrideFailed);
    }

    private async Task<List<ImportRowResult>> TryGetBulkFailuresAsync(
        string targetOrg,
        string jobId,
        ImportPlan plan,
        IReadOnlyList<string> headers,
        CancellationToken cancellationToken)
    {
        try
        {
            var arguments = new List<string>
            {
                "data", "bulk", "results",
                "--target-org", targetOrg,
                "--job-id", jobId,
                "--json",
            };

            var run = await _runner.RunAsync(arguments, timeout: TimeSpan.FromMinutes(5), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!run.Success)
            {
                _log.Warn($"Bulk 結果の取得に失敗: {run.ExitCode}");
                return new List<ImportRowResult>();
            }

            var records = ImportResultMapper.ExtractRecords(run.StdOut);
            var originalColumns = headers.Where(h => !h.StartsWith("sf__", StringComparison.OrdinalIgnoreCase)).ToList();
            return ImportResultMapper.MapBulkFailures(records, plan, originalColumns);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warn($"Bulk 結果の取得に失敗: {ex.Message}");
            return new List<ImportRowResult>();
        }
    }

    private static ImportRunResult BuildResult(
        ImportPlan plan,
        List<ImportRowResult> results,
        string? jobId,
        TimeSpan duration,
        int? overrideSuccess,
        int? overrideFailed)
    {
        var skipped = plan.Rows.Count(r => r.Error is not null);
        int success;
        int failed;

        if (overrideSuccess is not null || overrideFailed is not null)
        {
            success = overrideSuccess ?? results.Count(r => r.Success);
            failed = overrideFailed ?? results.Count(r => !r.Success);
        }
        else
        {
            success = results.Count(r => r.Success);
            failed = results.Count(r => !r.Success && r.RowIndex >= 0);
            failed += results.Count(r => !r.Success && r.RowIndex < 0);
        }

        return new ImportRunResult(plan.Rows.Count, success, failed, skipped, results, jobId, duration);
    }

    private static string DescribeFailure(SfCliResult run)
    {
        if (run.TimedOut)
        {
            return UiText.T("DataIo_Err_CliTimeoutFmt", run.Duration.TotalMinutes.ToString("0.#"));
        }

        var text = (string.IsNullOrWhiteSpace(run.StdErr) ? run.StdOut : run.StdErr).Trim();
        if (text.Length > 500)
        {
            text = text[..500] + "…";
        }

        return UiText.T("DataIo_Err_CliFailedFmt", run.ExitCode, text);
    }
}
