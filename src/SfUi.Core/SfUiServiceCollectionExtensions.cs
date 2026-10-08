using Microsoft.Extensions.DependencyInjection;

namespace SfUi.Core;

/// <summary>
/// SfUi のコアサービスを DI コンテナへ登録する。後続フェーズでサービスを追加していく。
/// </summary>
public static class SfUiServiceCollectionExtensions
{
    public static IServiceCollection AddSfUiCore(this IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton<AppLog>();
        services.AddSingleton(sp => new SfCliRunner(sp.GetRequiredService<AppSettingsStore>().Current.SfExecutablePath));
        services.AddSingleton<OrgService>();
        services.AddSingleton<SalesforceRestClient>();
        services.AddSingleton<AiChatClient>();
        services.AddSingleton<SoqlService>();
        services.AddSingleton<ApexService>();
        services.AddSingleton<ToolLauncherService>();
        services.AddSingleton<DeployService>();
        services.AddSingleton<AppSettingsStore>();
        services.AddSingleton<RecentFoldersStore>();
        services.AddSingleton<RecentUrlsStore>();
        services.AddSingleton<FavoritesStore>();
        services.AddSingleton<HistoryStore>();
        services.AddSingleton<OrgInfoCacheStore>();
        services.AddSingleton<OrgInfoPreferencesStore>();
        services.AddSingleton<OrgInfoService>();
        services.AddSingleton<OrgInfoSearchService>();
        services.AddSingleton<OrgExportService>();
        services.AddSingleton<OrgCompareService>();
        services.AddSingleton<OrgCompareStateStore>();
        services.AddSingleton<SObjectDescribeService>();
        services.AddSingleton<OrgMetadataService>();
        services.AddSingleton<DataExportService>();
        services.AddSingleton<DataImportService>();
        services.AddSingleton<PermissionAccessService>();
        services.AddSingleton<RecordAccessService>();
        services.AddSingleton<SalesforceSoapClient>();
        services.AddSingleton<BackupStateStore>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<BackupCompareService>();
        services.AddSingleton<OrgManageService>();
        services.AddSingleton<OrgManageStateStore>();
        services.AddSingleton<MigrationInventoryService>();
        return services;
    }
}
