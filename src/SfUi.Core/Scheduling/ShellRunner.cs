using System.Diagnostics;
using System.Text;

namespace SfUi.Core;

/// <summary>OS コマンドライン実行の結果。</summary>
public sealed record ShellResult(
    string CommandLine,
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut,
    TimeSpan Duration)
{
    /// <summary>正常終了（終了コード 0 かつタイムアウトなし）。</summary>
    public bool Success => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// 任意の OS コマンドラインを実行する（スケジューラの OS コマンドライン ジョブ用）。
/// Windows は cmd.exe /d /s /c、macOS / Linux は /bin/sh -c 経由。
/// </summary>
public sealed class ShellRunner
{
    /// <summary>保持する標準出力の上限（文字）。</summary>
    public const int MaxOutputChars = 200_000;

    private readonly AppLog _log;

    public ShellRunner(AppLog log)
    {
        _log = log;
    }

    /// <summary>コマンドラインを実行する。</summary>
    public async Task<ShellResult> RunAsync(
        string commandLine,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            throw new ArgumentException(UiText.T("Sched_ErrorNoCommand"), nameof(commandLine));
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = BuildStartInfo(commandLine, workingDirectory) };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _log.Warn($"コマンドラインを開始できませんでした ({commandLine}): {ex.Message}");
            return new ShellResult(commandLine, -1, string.Empty, ex.Message, false, stopwatch.Elapsed);
        }

        using var timeoutSource = timeout is { } limit
            ? new CancellationTokenSource(limit)
            : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        var stdoutTask = ReadAsync(process.StandardOutput, linked.Token);
        var stderrTask = ReadAsync(process.StandardError, linked.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            TryKill(process);
            if (!cancellationToken.IsCancellationRequested)
            {
                // タイムアウト時は出力を回収する時間を与える
                await WaitQuietlyAsync(process).ConfigureAwait(false);
            }
        }

        var stdout = await SafeAwaitAsync(stdoutTask).ConfigureAwait(false);
        var stderr = await SafeAwaitAsync(stderrTask).ConfigureAwait(false);

        if (!cancellationToken.IsCancellationRequested && !process.HasExited)
        {
            TryKill(process);
        }

        var exitCode = -1;
        try
        {
            if (process.HasExited)
            {
                exitCode = process.ExitCode;
            }
        }
        catch (InvalidOperationException)
        {
            // 起動に失敗した場合など
        }

        stopwatch.Stop();
        return new ShellResult(commandLine, exitCode, stdout, stderr, timedOut, stopwatch.Elapsed);
    }

    private static ProcessStartInfo BuildStartInfo(string commandLine, string? workingDirectory)
    {
        var isWindows = OperatingSystem.IsWindows();
        var info = new ProcessStartInfo
        {
            FileName = isWindows ? (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (isWindows)
        {
            info.ArgumentList.Add("/d");
            info.ArgumentList.Add("/s");
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add(commandLine);
        }
        else
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(commandLine);
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            info.WorkingDirectory = workingDirectory;
        }

        return info;
    }

    private static async Task<string> ReadAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[8192];
        var builder = new StringBuilder();
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (builder.Length < MaxOutputChars)
                {
                    builder.Append(buffer, 0, Math.Min(read, MaxOutputChars - builder.Length));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // タイムアウト / キャンセルで打ち切る
        }
        catch (ObjectDisposedException)
        {
            // プロセス終了後にストリームが閉じられた
        }
        catch (IOException)
        {
            // パイプ切断
        }

        return builder.ToString();
    }

    private static async Task<string> SafeAwaitAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static async Task WaitQuietlyAsync(Process process)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 無視
        }
        catch (InvalidOperationException)
        {
            // 既に終了
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // 既に終了 / 権限なし
        }
    }
}
