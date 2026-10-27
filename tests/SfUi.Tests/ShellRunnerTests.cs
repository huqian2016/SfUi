using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>OS コマンドライン実行（ShellRunner）のテスト。</summary>
public class ShellRunnerTests : IDisposable
{
    private readonly string _sandbox;
    private readonly AppPaths _paths;

    public ShellRunnerTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _paths = AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    private ShellRunner CreateRunner() => new(new AppLog(_paths));

    [Fact]
    public async Task RunAsync_CapturesStdOut()
    {
        var runner = CreateRunner();

        var result = await runner.RunAsync("echo sfui-shell-test");

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("sfui-shell-test", result.StdOut);
    }

    [Fact]
    public async Task RunAsync_ReturnsFailureExitCode()
    {
        var runner = CreateRunner();
        // Windows は cmd.exe、macOS は sh なので終了コード 3 を返す式を使う
        var command = OperatingSystem.IsWindows() ? "exit /b 3" : "exit 3";

        var result = await runner.RunAsync(command);

        Assert.False(result.Success);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_UsesWorkingDirectory()
    {
        var runner = CreateRunner();
        var command = OperatingSystem.IsWindows() ? "cd" : "pwd";

        var result = await runner.RunAsync(command, workingDirectory: _sandbox);

        Assert.True(result.Success);
        Assert.Contains("sfui-tests", result.StdOut);
    }

    [Fact]
    public async Task RunAsync_EmptyCommand_Throws()
    {
        var runner = CreateRunner();

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync("  "));
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsProcess()
    {
        var runner = CreateRunner();
        var command = OperatingSystem.IsWindows() ? "ping -n 20 127.0.0.1 >nul" : "sleep 20";

        var result = await runner.RunAsync(command, timeout: TimeSpan.FromMilliseconds(500));

        Assert.True(result.TimedOut);
        Assert.False(result.Success);
    }
}
