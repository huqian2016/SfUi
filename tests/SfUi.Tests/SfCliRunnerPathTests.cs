using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class SfCliRunnerPathTests : IDisposable
{
    private readonly string _sandbox;

    public SfCliRunnerPathTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
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

    [Fact]
    public void ResolveSfPath_UsesExplicitPath_WhenFileExists()
    {
        var fake = Path.Combine(_sandbox, "sf.cmd");
        File.WriteAllText(fake, "");

        Assert.Equal(fake, SfCliRunner.ResolveSfPath(fake));
    }

    [Fact]
    public void ResolveSfPath_IgnoresExplicitPath_WhenFileMissing()
    {
        var missing = Path.Combine(_sandbox, "not-exists.cmd");

        var resolved = SfCliRunner.ResolveSfPath(missing);

        Assert.NotEqual(missing, resolved);
    }

    [Fact]
    public void SetExecutablePath_ReturnsResolvedPath()
    {
        var fake = Path.Combine(_sandbox, "sf.cmd");
        File.WriteAllText(fake, "");
        var runner = new SfCliRunner();

        var resolved = runner.SetExecutablePath(fake);

        Assert.Equal(fake, resolved);
        Assert.Equal(fake, runner.SfExecutablePath);
    }

    [Fact]
    public void SetExecutablePath_WithMissingFile_FallsBackToAutoDetection()
    {
        var runner = new SfCliRunner();
        var missing = Path.Combine(_sandbox, "not-exists.cmd");

        var resolved = runner.SetExecutablePath(missing);

        Assert.NotEqual(missing, resolved);
    }
}
