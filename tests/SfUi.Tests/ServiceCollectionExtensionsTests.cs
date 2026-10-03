using Microsoft.Extensions.DependencyInjection;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSfUiCore_RegistersAppPathsAndAppLogAsSingletons()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);

        try
        {
            var paths = AppPaths.Resolve(dataRootOverride: sandbox, baseDirectory: sandbox, appDataDirectory: sandbox);

            var services = new ServiceCollection();
            services.AddSfUiCore(paths);
            using var provider = services.BuildServiceProvider();

            Assert.Same(paths, provider.GetRequiredService<AppPaths>());
            Assert.NotNull(provider.GetRequiredService<AppLog>());
            Assert.Same(provider.GetRequiredService<AppLog>(), provider.GetRequiredService<AppLog>());
        }
        finally
        {
            try
            {
                Directory.Delete(sandbox, recursive: true);
            }
            catch
            {
                // 後始末の失敗はテスト結果に影響させない
            }
        }
    }

    [Fact]
    public void AddSfUiCore_RegistersOrgInfoServicesAsSingletons()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);

        try
        {
            var paths = AppPaths.Resolve(dataRootOverride: sandbox, baseDirectory: sandbox, appDataDirectory: sandbox);

            var services = new ServiceCollection();
            services.AddSfUiCore(paths);
            using var provider = services.BuildServiceProvider();

            var cacheStore = provider.GetRequiredService<OrgInfoCacheStore>();
            Assert.Same(cacheStore, provider.GetRequiredService<OrgInfoCacheStore>());
            Assert.NotNull(provider.GetRequiredService<OrgInfoPreferencesStore>());
            Assert.NotNull(provider.GetRequiredService<OrgInfoService>());
            Assert.NotNull(provider.GetRequiredService<OrgInfoSearchService>());
        }
        finally
        {
            try
            {
                Directory.Delete(sandbox, recursive: true);
            }
            catch
            {
                // 後始末の失敗はテスト結果に影響させない
            }
        }
    }
}
