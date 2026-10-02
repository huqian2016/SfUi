using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class AppSettingsStoreTests : IDisposable
{
    private readonly string _sandbox;

    public AppSettingsStoreTests()
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

    private AppPaths Paths => AppPaths.Resolve(dataRootOverride: _sandbox, baseDirectory: _sandbox, appDataDirectory: _sandbox);
    private AppLog Log => new(Paths);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new AppSettingsStore(Paths, Log);

        Assert.Equal(2000, store.Current.MaxHistoryPerType);
        Assert.Equal(64 * 1024, store.Current.ResultInlineThresholdBytes);
        Assert.Equal(ConfirmPolicies.Dangerous, store.Current.ConfirmPolicy);
        Assert.Null(store.Current.SfExecutablePath);
        Assert.Null(store.Current.LastFolder);
        Assert.Null(store.Current.LastOrgUsername);
    }

    [Fact]
    public void SaveAndReload_RoundTripsValues()
    {
        var store = new AppSettingsStore(Paths, Log);
        store.Current.LastFolder = @"C:\work\hks4";
        store.Current.LastOrgUsername = "ko-cpyg@force.com";
        store.Current.MaxHistoryPerType = 500;
        store.Save();

        var reloaded = new AppSettingsStore(Paths, Log);

        Assert.Equal(@"C:\work\hks4", reloaded.Current.LastFolder);
        Assert.Equal("ko-cpyg@force.com", reloaded.Current.LastOrgUsername);
        Assert.Equal(500, reloaded.Current.MaxHistoryPerType);
    }

    [Fact]
    public void Load_CorruptPrimary_FallsBackToBackup()
    {
        var store = new AppSettingsStore(Paths, Log);
        store.Current.LastFolder = @"C:\work\A";
        store.Save(); // 初回保存（メインのみ）
        store.Current.LastFolder = @"C:\work\B";
        store.Save(); // 2 回目（.bak = A 版）

        File.WriteAllText(Paths.SettingsFile, "{ broken json");

        var reloaded = new AppSettingsStore(Paths, Log);

        Assert.Equal(@"C:\work\A", reloaded.Current.LastFolder);
    }

    [Fact]
    public void Load_CorruptEverything_ReturnsDefaultsAndPreservesBadFile()
    {
        File.WriteAllText(Paths.SettingsFile, "not json");
        File.WriteAllText(Paths.SettingsFile + ".bak", "also not json");

        var store = new AppSettingsStore(Paths, Log);

        Assert.Equal(2000, store.Current.MaxHistoryPerType);
        Assert.Single(Directory.GetFiles(_sandbox, "settings.json.bad-*"));
    }
}
