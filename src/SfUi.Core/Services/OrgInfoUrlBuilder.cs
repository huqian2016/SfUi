namespace SfUi.Core;

/// <summary>
/// Salesforce Setup（Lightning）ページの URL を組み立てる。
/// パスはリリース・リージョンで変わり得るため、実装時に主要リンクを実機検証する（docs/org-info-window-plan.md）。
/// </summary>
public static class OrgInfoUrlBuilder
{
    public static string SetupHome(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/SetupOneHome/home");

    public static string CompanyInformation(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/CompanyProfileInfo/home");

    public static string Users(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/ManageUsers/home");

    public static string UserDetail(string instanceUrl, string userId) => Combine(instanceUrl, $"/lightning/setup/ManageUsers/page?address=/{userId}");

    public static string Profiles(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/Profiles/home");

    public static string ProfileDetail(string instanceUrl, string profileId) => Combine(instanceUrl, $"/lightning/setup/Profiles/page?address=/{profileId}");

    public static string PermissionSets(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/PermSets/home");

    public static string Roles(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/Roles/home");

    public static string SharingSettings(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/SecuritySharing/home");

    public static string ObjectManager(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/ObjectManager/home");

    public static string ObjectDetail(string instanceUrl, string objectApiName) =>
        Combine(instanceUrl, $"/lightning/setup/ObjectManager/{objectApiName}/Details/view");

    public static string ObjectFields(string instanceUrl, string objectApiName) =>
        Combine(instanceUrl, $"/lightning/setup/ObjectManager/{objectApiName}/FieldsAndRelationships/view");

    public static string ApexClasses(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/ApexClasses/home");

    public static string ApexTriggers(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/ApexTriggers/home");

    public static string Flows(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/Flows/home");

    public static string ScheduledJobs(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/ScheduledJobs/home");

    public static string ConnectedApps(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/ConnectedApplication/home");

    public static string LoginHistory(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/LoginHistory/home");

    public static string SetupAuditTrail(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/SecurityAuditTrail/home");

    public static string InstalledPackages(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/ImportedPackage/home");

    public static string ResourceUsage(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/CompanyResourceUsage/home");

    public static string SessionSettings(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/SessionSettings/home");

    public static string PasswordPolicies(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/PasswordPolicies/home");

    public static string LoginIpRanges(string instanceUrl) => Combine(instanceUrl, "/lightning/setup/NetworkAccess/home");

    /// <summary>セクションに対応する Setup ページ（instanceUrl 未設定や未対応は null）。</summary>
    public static string? ForSection(string? instanceUrl, string sectionId)
    {
        if (string.IsNullOrWhiteSpace(instanceUrl))
        {
            return null;
        }

        try
        {
            return sectionId switch
            {
                OrgInfoSections.Overview => CompanyInformation(instanceUrl),
                OrgInfoSections.Settings => CompanyInformation(instanceUrl),
                OrgInfoSections.Users => Users(instanceUrl),
                OrgInfoSections.Profiles => Profiles(instanceUrl),
                OrgInfoSections.PermissionSets => PermissionSets(instanceUrl),
                OrgInfoSections.Roles => Roles(instanceUrl),
                OrgInfoSections.Objects => ObjectManager(instanceUrl),
                OrgInfoSections.Owds => SharingSettings(instanceUrl),
                OrgInfoSections.ApexClasses => ApexClasses(instanceUrl),
                OrgInfoSections.ApexTriggers => ApexTriggers(instanceUrl),
                OrgInfoSections.Flows => Flows(instanceUrl),
                OrgInfoSections.ScheduledJobs => ScheduledJobs(instanceUrl),
                OrgInfoSections.ConnectedApps => ConnectedApps(instanceUrl),
                OrgInfoSections.InstalledPackages => InstalledPackages(instanceUrl),
                OrgInfoSections.LoginHistory => LoginHistory(instanceUrl),
                OrgInfoSections.SetupAuditTrail => SetupAuditTrail(instanceUrl),
                OrgInfoSections.RecordTypes => ObjectManager(instanceUrl),
                _ => null,
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>オブジェクト項目ページ（instanceUrl 未設定は null）。</summary>
    public static string? ObjectFieldsOrNull(string? instanceUrl, string objectApiName) =>
        string.IsNullOrWhiteSpace(instanceUrl) || string.IsNullOrEmpty(objectApiName)
            ? null
            : ObjectFields(instanceUrl, objectApiName);

    private static string Combine(string instanceUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(instanceUrl))
        {
            throw new ArgumentException("instanceUrl is required.", nameof(instanceUrl));
        }

        return instanceUrl.TrimEnd('/') + path;
    }
}
