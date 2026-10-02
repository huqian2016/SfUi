using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class AppPathsTests : IDisposable
{
    private readonly string _sandbox;

    public AppPathsTests()
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
    public void Resolve_Override_UsesSpecifiedDirectoryAndCreatesSubDirectories()
    {
        var root = Path.Combine(_sandbox, "custom-data");

        var paths = AppPaths.Resolve(dataRootOverride: root, baseDirectory: _sandbox, appDataDirectory: _sandbox);

        Assert.Equal(Path.GetFullPath(root), paths.DataRoot);
        Assert.True(Directory.Exists(paths.LogsDirectory));
        Assert.True(Directory.Exists(paths.HistoryDirectory));
        Assert.True(Directory.Exists(paths.ResultsDirectory));
        Assert.True(Directory.Exists(paths.TempDirectory));
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "settings.json"), paths.SettingsFile);
    }

    [Fact]
    public void Resolve_DevelopmentTree_FindsSolutionRootAndUsesRepoDataFolder()
    {
        var repoRoot = Path.Combine(_sandbox, "repo");
        var exeDirectory = Path.Combine(repoRoot, "src", "SfUi.App", "bin", "Debug", "net9.0-windows");
        Directory.CreateDirectory(exeDirectory);
        File.WriteAllText(Path.Combine(repoRoot, "SfUi.sln"), string.Empty);

        var paths = AppPaths.Resolve(baseDirectory: exeDirectory, appDataDirectory: Path.Combine(_sandbox, "appdata"));

        Assert.Equal(Path.Combine(repoRoot, "data"), paths.DataRoot);
    }

    [Fact]
    public void Resolve_PortableLayout_PrefersDataFolderNextToExecutable()
    {
        var appDirectory = Path.Combine(_sandbox, "dist");
        Directory.CreateDirectory(appDirectory);

        var paths = AppPaths.Resolve(baseDirectory: appDirectory, appDataDirectory: Path.Combine(_sandbox, "appdata"));

        Assert.Equal(Path.Combine(appDirectory, "data"), paths.DataRoot);
    }
}
