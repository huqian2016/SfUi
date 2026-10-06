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
        Assert.Equal(UiText.English, store.Current.Language);
        Assert.Null(store.Current.SfExecutablePath);
        Assert.Null(store.Current.LastFolder);
        Assert.Null(store.Current.LastOrgUsername);
        Assert.False(store.Current.WelcomeDismissed);
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
    public void SaveAndReload_RoundTripsWelcomeDismissed()
    {
        var store = new AppSettingsStore(Paths, Log);
        store.Current.WelcomeDismissed = true;
        store.Save();

        var reloaded = new AppSettingsStore(Paths, Log);

        Assert.True(reloaded.Current.WelcomeDismissed);
    }

    [Fact]
    public void Load_LegacyFileWithoutWelcomeDismissed_DefaultsToFalse()
    {
        File.WriteAllText(Paths.SettingsFile, "{\"language\":\"ja\"}");

        var store = new AppSettingsStore(Paths, Log);

        Assert.False(store.Current.WelcomeDismissed);
        Assert.Equal(UiText.Japanese, store.Current.Language);
    }

    [Fact]
    public void SaveAndReload_RoundTripsAiSettings()
    {
        var store = new AppSettingsStore(Paths, Log);
        store.Current.AiEndpoint = "https://api.openai.com/v1/chat/completions";
        store.Current.AiApiKey = "sk-test";
        store.Current.AiModel = "gpt-4o-mini";
        store.Save();

        var reloaded = new AppSettingsStore(Paths, Log);

        Assert.Equal("https://api.openai.com/v1/chat/completions", reloaded.Current.AiEndpoint);
        Assert.Equal("gpt-4o-mini", reloaded.Current.AiModel);
        // キーは保存時に難読化（enc1:）され、使用時に復元される
        Assert.NotNull(reloaded.Current.AiApiKey);
        Assert.StartsWith(AiKeyObfuscation.Prefix, reloaded.Current.AiApiKey);
        Assert.Equal("sk-test", AiKeyObfuscation.Normalize(reloaded.Current.AiApiKey));
    }

    [Fact]
    public void Save_ObfuscatesAiApiKey_OnDisk()
    {
        var store = new AppSettingsStore(Paths, Log);
        store.Current.AiApiKey = "sk-plain";
        store.Save();

        Assert.StartsWith(AiKeyObfuscation.Prefix, store.Current.AiApiKey);
        Assert.DoesNotContain("sk-plain", File.ReadAllText(Paths.SettingsFile));
    }

    [Fact]
    public void Load_MigratesLegacyDeepSeekFields_ToGenericAiFields()
    {
        File.WriteAllText(Paths.SettingsFile, """
            {
              "language": "ja",
              "deepSeekApiKey": "sk-old",
              "deepSeekModel": "deepseek-reasoner"
            }
            """);

        var store = new AppSettingsStore(Paths, Log);

        Assert.Equal("sk-old", store.Current.AiApiKey);
        Assert.Equal("deepseek-reasoner", store.Current.AiModel);
        Assert.Null(store.Current.DeepSeekApiKey);
        Assert.Null(store.Current.DeepSeekModel);
    }

    [Fact]
    public void Load_LegacyDeepSeekFields_DoNotOverrideNewAiFields()
    {
        File.WriteAllText(Paths.SettingsFile, """
            {
              "aiApiKey": "sk-new",
              "aiModel": "gpt-4o-mini",
              "deepSeekApiKey": "sk-old",
              "deepSeekModel": "deepseek-reasoner"
            }
            """);

        var store = new AppSettingsStore(Paths, Log);

        Assert.Equal("sk-new", store.Current.AiApiKey);
        Assert.Equal("gpt-4o-mini", store.Current.AiModel);
        Assert.Null(store.Current.DeepSeekApiKey);
        Assert.Null(store.Current.DeepSeekModel);
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
