using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SfUi.Core;

/// <summary>sf コマンドの実行結果（生データ）。</summary>
public sealed record SfCliResult(
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut,
    TimeSpan Duration)
{
    /// <summary>正常終了（ExitCode 0 かつタイムアウトなし）か。</summary>
    public bool Success => ExitCode == 0 && !TimedOut;

    /// <summary>表示用コマンドライン（例: sf org list --json）。</summary>
    public string CommandLine => "sf " + string.Join(' ', Arguments);
}

/// <summary>
/// Salesforce CLI (sf) を子プロセスとして実行する。
/// .cmd は CreateProcess で直接起動できないため、cmd.exe /d /s /c 経由で実行する。
/// </summary>
public sealed class SfCliRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private string? _sfExecutablePath;

    /// <summary>検出した sf 実行ファイルのパス（未検出時は null）。</summary>
    public string? SfExecutablePath => _sfExecutablePath;

    public SfCliRunner()
        : this(null)
    {
    }

    /// <summary>設定で指定された sf パスを優先して実行ファイルを解決する（null / 空なら自動検出）。</summary>
    public SfCliRunner(string? configuredPath)
    {
        _sfExecutablePath = ResolveSfPath(configuredPath);
    }

    /// <summary>sf 実行ファイルを差し替える（設定画面から）。戻り値は解決結果（未検出時 null）。</summary>
    public string? SetExecutablePath(string? configuredPath)
    {
        _sfExecutablePath = ResolveSfPath(configuredPath);
        return _sfExecutablePath;
    }

    /// <summary>
    /// sf の実行ファイルを解決する。優先順位:
    /// 1) 引数の明示パス（設定画面の値）  2) 環境変数 SFUI_SF_PATH  3) %ProgramFiles%\sf\bin\sf.cmd  4) PATH 上の sf.cmd / sf.exe
    /// </summary>
    public static string? ResolveSfPath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        var overridePath = Environment.GetEnvironmentVariable("SFUI_SF_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var installed = Path.Combine(programFiles, "sf", "bin", "sf.cmd");
        if (File.Exists(installed))
        {
            return installed;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var fileName in new[] { "sf.cmd", "sf.exe" })
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>sf を実行して結果を返す。</summary>
    /// <param name="arguments">sf に渡す引数（"org", "list", "--json" など）。</param>
    /// <param name="workingDirectory">カレントディレクトリ（SF 実行フォルダ）。null ならプロセス既定。</param>
    /// <param name="timeout">タイムアウト（既定 120 秒）。null で既定値。</param>
    /// <param name="cancellationToken">キャンセル用トークン。</param>
    public async Task<SfCliResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (_sfExecutablePath is null)
        {
            throw new InvalidOperationException(UiText.T("Core_SfNotFound"));
        }

        var commandLine = BuildCommandLine(_sfExecutablePath, arguments);
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = ToCmdArguments(commandLine),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        startInfo.Environment["SF_DISABLE_TELEMETRY"] = "true";
        startInfo.Environment["SF_AUTOUPDATE_DISABLE"] = "true";

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(UiText.T("Core_SfStartFailedFmt", ex.Message), ex);
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        var timedOut = false;
        var effectiveTimeout = timeout ?? DefaultTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (effectiveTimeout != Timeout.InfiniteTimeSpan)
        {
            timeoutCts.CancelAfter(effectiveTimeout);
        }

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillTree(process);
            timedOut = !cancellationToken.IsCancellationRequested;
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // kill 後の待機失敗は無視
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        var stdOut = await SafeReadAsync(stdOutTask).ConfigureAwait(false);
        var stdErr = await SafeReadAsync(stdErrTask).ConfigureAwait(false);
        stopwatch.Stop();

        if (timedOut && string.IsNullOrEmpty(stdErr))
        {
            stdErr = UiText.T("Core_Timeout");
        }

        return new SfCliResult(arguments.ToArray(), SafeGetExitCode(process), stdOut.TrimEnd(), stdErr.TrimEnd(), timedOut, stopwatch.Elapsed);
    }

    /// <summary>実行ファイルパスと引数からコマンドライン文字列を組み立てる。</summary>
    public static string BuildCommandLine(string executablePath, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        builder.Append(QuoteArgument(executablePath));
        foreach (var argument in arguments)
        {
            builder.Append(' ').Append(QuoteArgument(argument));
        }

        return builder.ToString();
    }

    /// <summary>cmd.exe /d /s /c に渡す引数文字列へ変換する。</summary>
    public static string ToCmdArguments(string commandLine) => $"/d /s /c \"{commandLine}\"";

    /// <summary>Windows のコマンドライン規則で 1 引数を引用符処理する。</summary>
    public static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
        {
            return argument;
        }

        var builder = new StringBuilder();
        builder.Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1).Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes).Append(c);
            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2).Append('"');
        return builder.ToString();
    }

    private static async Task<string> SafeReadAsync(Task<string> readTask)
    {
        try
        {
            return await readTask.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 既に終了している場合は無視
        }
    }

    private static int SafeGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }
}
