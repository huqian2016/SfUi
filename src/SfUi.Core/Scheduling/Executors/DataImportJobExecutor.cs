using System.Globalization;

namespace SfUi.Core;

/// <summary>CSV を取り込む（REST バッチ / Bulk API 2.0）Executor。</summary>
public sealed class DataImportJobExecutor : IScheduledJobExecutor
{
    private readonly DataImportService _import;
    private readonly SObjectDescribeService _describe;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public DataImportJobExecutor(DataImportService import, SObjectDescribeService describe, AppPaths paths, AppLog log)
    {
        _import = import;
        _describe = describe;
        _paths = paths;
        _log = log;
    }

    public string JobType => ScheduledJobTypes.DataImport;

    public async Task<JobRunResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        var payload = job.GetPayload<DataImportJobPayload>();
        if (string.IsNullOrWhiteSpace(payload.ObjectName))
        {
            return JobRunResult.Fail(UiText.T("Sched_ValidationObjectRequired"));
        }

        if (string.IsNullOrWhiteSpace(payload.CsvPath))
        {
            return JobRunResult.Fail(UiText.T("Sched_ValidationCsvRequired"));
        }

        if (!File.Exists(payload.CsvPath))
        {
            return JobRunResult.Fail(UiText.T("Sched_ErrorCsvNotFoundFmt", payload.CsvPath));
        }

        if (string.IsNullOrWhiteSpace(job.TargetOrg))
        {
            return JobRunResult.Fail(UiText.T("Sched_ErrorNoOrg"));
        }

        try
        {
            var (text, encoding) = CsvParser.ReadFile(payload.CsvPath);
            var csv = CsvParser.Parse(text);
            if (csv.Rows.Count == 0)
            {
                return JobRunResult.Fail(UiText.T("Sched_ErrorCsvEmptyFmt", payload.CsvPath));
            }

            var describe = await _describe.DescribeAsync(job.TargetOrg, payload.ObjectName, cancellationToken: cancellationToken);
            var mappings = csv.Headers
                .Select((header, index) =>
                {
                    var field = describe.Fields.FirstOrDefault(f =>
                        string.Equals(f.Name, header.Trim(), StringComparison.OrdinalIgnoreCase));
                    return new ImportColumnMapping(index, header, field?.Name, field is not null);
                })
                .ToList();

            var operation = ScheduledImportOperations.ToOperation(payload.Operation);
            var externalId = string.IsNullOrWhiteSpace(payload.ExternalIdField) ? null : payload.ExternalIdField!.Trim();
            var plan = ImportBatchPlanner.BuildPlan(csv.Rows, mappings, describe, operation, externalId, payload.EmptyAsNull);

            ImportRunResult result;
            if (payload.UseBulk)
            {
                Directory.CreateDirectory(_paths.TempDirectory);
                var tempPath = Path.Combine(_paths.TempDirectory, $"sched-import-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.csv");
                try
                {
                    result = await _import.RunBulkAsync(
                        job.TargetOrg,
                        describe.Name,
                        operation,
                        externalId,
                        plan,
                        tempPath,
                        progress: null,
                        cancellationToken);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch (IOException)
                    {
                        // 一時ファイルの削除失敗は無視
                    }
                }
            }
            else
            {
                result = await _import.RunRestAsync(
                    job.TargetOrg,
                    describe.Name,
                    operation,
                    externalId,
                    plan,
                    progress: null,
                    cancellationToken);
            }

            var summary = UiText.T(
                "Sched_SummaryImportFmt",
                result.SuccessCount,
                result.FailedCount,
                result.SkippedCount);

            var output = new System.Text.StringBuilder();
            output.AppendLine($"object={describe.Name} operation={operation} rows={csv.Rows.Count} encoding={encoding} bulk={payload.UseBulk}");
            output.AppendLine($"success={result.SuccessCount} failed={result.FailedCount} skipped={result.SkippedCount} / {result.Duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)}s");

            foreach (var row in result.Rows.Where(r => !r.Success).Take(20))
            {
                output.AppendLine($"row {row.RowIndex}: {row.Error}");
            }

            return result.FailedCount == 0
                ? JobRunResult.Ok(summary, JobOutput.Truncate(output.ToString()))
                : JobRunResult.Fail(summary, UiText.T("Sched_SummaryImportFmt", result.SuccessCount, result.FailedCount, result.SkippedCount), JobOutput.Truncate(output.ToString()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"スケジュール データ投入ジョブが失敗しました ({job.DisplayName}): {ex.Message}");
            return JobRunResult.Fail(UiText.T("Sched_SummaryFailedFmt", ex.Message), ex.Message);
        }
    }
}
