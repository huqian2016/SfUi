using System.Text;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>匿名Apex 実行の結果。</summary>
public sealed class ApexExecutionResult
{
    public bool Success { get; init; }

    public bool Compiled { get; init; }

    public string? CompileProblem { get; init; }

    public string? ExceptionMessage { get; init; }

    public string? ExceptionStackTrace { get; init; }

    /// <summary>実行デバッグログ全文（sf apex run の logs）。</summary>
    public string? Logs { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }

    /// <summary>コマンド自体が失敗した場合のメッセージ。</summary>
    public string? ErrorMessage { get; init; }

    public TimeSpan Duration { get; init; }

    public string RawJson { get; init; } = "";
}

/// <summary>デバッグログ一覧の 1 件。</summary>
public sealed class ApexLogInfo
{
    public string Id { get; init; } = "";

    public string? StartTime { get; init; }

    public string? Operation { get; init; }

    public string? Status { get; init; }

    public string? Application { get; init; }

    public int? DurationMs { get; init; }

    public long? LogLength { get; init; }

    public string? LogUserId { get; init; }

    public string StartTimeLabel => StartTime ?? "";
}

/// <summary>匿名Apex 実行とデバッグログ取得。</summary>
public sealed class ApexService
{
    private static readonly TimeSpan ExecuteTimeout = TimeSpan.FromMinutes(5);

    private readonly SfCliRunner _runner;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public ApexService(SfCliRunner runner, AppPaths paths, AppLog log)
    {
        _runner = runner;
        _paths = paths;
        _log = log;
    }

    /// <summary>匿名Apex を一時ファイル経由で実行する。</summary>
    public async Task<ApexExecutionResult> ExecuteAnonymousAsync(
        string targetOrg,
        string code,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetOrg))
        {
            throw new ArgumentException(UiText.T("Core_OrgSelectRequired"), nameof(targetOrg));
        }

        Directory.CreateDirectory(_paths.TempDirectory);
        var tempFile = Path.Combine(_paths.TempDirectory, $"anonymous-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.apex");
        await File.WriteAllTextAsync(tempFile, code, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);

        try
        {
            var raw = await _runner.RunAsync(
                new[] { "apex", "run", "--target-org", targetOrg, "--file", tempFile, "--json" },
                workingDirectory,
                timeout: ExecuteTimeout,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return ParseRunResult(raw);
        }
        finally
        {
            try
            {
                File.Delete(tempFile);
            }
            catch
            {
                // 一時ファイル削除の失敗は無視
            }
        }
    }

    /// <summary>sf apex run --json の出力を解析する。</summary>
    public static ApexExecutionResult ParseRunResult(SfCliResult raw)
    {
        var command = SfCommandResult.From(raw);
        if (command.Result is not { } result || result.ValueKind != JsonValueKind.Object)
        {
            return new ApexExecutionResult
            {
                ErrorMessage = command.ErrorMessage ?? UiText.T("Core_ApexRunFailed"),
                RawJson = raw.StdOut,
                Duration = raw.Duration,
            };
        }

        return new ApexExecutionResult
        {
            Success = GetBool(result, "success"),
            Compiled = GetBool(result, "compiled"),
            CompileProblem = EmptyToNull(GetString(result, "compileProblem")),
            ExceptionMessage = EmptyToNull(GetString(result, "exceptionMessage")),
            ExceptionStackTrace = EmptyToNull(GetString(result, "exceptionStackTrace")),
            Logs = EmptyToNull(GetString(result, "logs")),
            Line = GetInt(result, "line"),
            Column = GetInt(result, "column"),
            ErrorMessage = command.IsSuccess ? null : command.ErrorMessage,
            RawJson = raw.StdOut,
            Duration = raw.Duration,
        };
    }

    /// <summary>org に保存されているデバッグログの一覧を取得する（新しい順）。</summary>
    public async Task<IReadOnlyList<ApexLogInfo>> ListLogsAsync(string targetOrg, CancellationToken cancellationToken = default)
    {
        var raw = await _runner
            .RunAsync(new[] { "apex", "list", "log", "--target-org", targetOrg, "--json" }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var command = SfCommandResult.From(raw);
        if (!command.IsSuccess || command.Result is not { } result || result.ValueKind != JsonValueKind.Array)
        {
            throw new SfCliException(UiText.T("Core_LogListFailedFmt", command.ErrorMessage), raw);
        }

        return ParseLogList(result);
    }

    /// <summary>sf apex list log --json の result 配列を解析する。</summary>
    public static IReadOnlyList<ApexLogInfo> ParseLogList(JsonElement array)
    {
        var logs = new List<ApexLogInfo>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            logs.Add(new ApexLogInfo
            {
                Id = GetString(item, "Id") ?? GetString(item, "id") ?? "",
                StartTime = GetString(item, "StartTime") ?? GetString(item, "startTime"),
                Operation = GetString(item, "Operation") ?? GetString(item, "operation"),
                Status = GetString(item, "Status") ?? GetString(item, "status"),
                Application = GetString(item, "Application") ?? GetString(item, "application"),
                DurationMs = GetInt(item, "DurationMilliseconds") ?? GetInt(item, "durationMilliseconds"),
                LogLength = GetLong(item, "LogLength") ?? GetLong(item, "logLength"),
                LogUserId = GetString(item, "LogUserId") ?? GetString(item, "logUserId"),
            });
        }

        return logs
            .OrderByDescending(l => DateTimeOffset.TryParse(l.StartTime, out var t) ? t : DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>
    /// デバッグログ本文を取得する。logId 省略時は直近 latestCount 件のうち先頭（最新）を返す。
    /// ログが無い場合は null。
    /// </summary>
    public async Task<string?> GetLogAsync(string targetOrg, string? logId = null, int latestCount = 1, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "apex", "get", "log", "--target-org", targetOrg, "--json" };
        if (!string.IsNullOrWhiteSpace(logId))
        {
            arguments.Add("--log-id");
            arguments.Add(logId);
        }
        else
        {
            arguments.Add("--number");
            arguments.Add(latestCount.ToString());
        }

        var raw = await _runner.RunAsync(arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        var command = SfCommandResult.From(raw);
        if (!command.IsSuccess || command.Result is not { } result)
        {
            throw new SfCliException(UiText.T("Core_LogFetchFailedFmt", command.ErrorMessage), raw);
        }

        return ExtractLogText(result);
    }

    /// <summary>
    /// sf apex get log --json の result からログ本文を取り出す。
    /// result が文字列 / { log: "..." } / [ { log: "..." } ] のいずれでも対応する。
    /// </summary>
    public static string? ExtractLogText(JsonElement result)
    {
        switch (result.ValueKind)
        {
            case JsonValueKind.String:
                return result.GetString();

            case JsonValueKind.Array:
                foreach (var item in result.EnumerateArray())
                {
                    var text = ExtractLogText(item);
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }

                return null;

            case JsonValueKind.Object:
                if (result.TryGetProperty("log", out var log))
                {
                    return ExtractLogText(log);
                }

                return null;

            default:
                return null;
        }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    private static int? GetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static long? GetLong(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
}
