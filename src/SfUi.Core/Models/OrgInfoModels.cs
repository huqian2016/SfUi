namespace SfUi.Core;

/// <summary>組織情報セクションの ID・表示定義（タブ構成・列・項目カタログ）。</summary>
public static class OrgInfoSections
{
    public const string Overview = "overview";
    public const string Settings = "settings";
    public const string Users = "users";
    public const string Profiles = "profiles";
    public const string PermissionSets = "permissionSets";
    public const string Roles = "roles";
    public const string Objects = "objects";
    public const string Owds = "owds";

    // ---- 追加候補セクション（Step 4）----
    public const string ApexClasses = "apexClasses";
    public const string ApexTriggers = "apexTriggers";
    public const string Flows = "flows";
    public const string ScheduledJobs = "scheduledJobs";
    public const string ConnectedApps = "connectedApps";
    public const string InstalledPackages = "installedPackages";
    public const string LoginHistory = "loginHistory";
    public const string SetupAuditTrail = "setupAuditTrail";
    public const string RecordTypes = "recordTypes";
    public const string Currencies = "currencies";

    /// <summary>オブジェクト項目（遅延取得）セクションの ID 接頭辞。例: fields:Account</summary>
    public const string FieldsPrefix = "fields:";

    /// <summary>オブジェクト項目タブのタイトル（キー）。</summary>
    public const string FieldsTitleKey = "OrgInfo_Tab_Fields";

    public static string Fields(string objectApiName) => FieldsPrefix + objectApiName;

    public static bool IsFieldsSection(string sectionId) => sectionId.StartsWith(FieldsPrefix, StringComparison.Ordinal);

    /// <summary>セクション定義。AutoFetch = 初回オープン時の自動取得対象。</summary>
    public sealed record Definition(string Id, string TitleKey, bool AutoFetch);

    public static IReadOnlyList<Definition> All { get; } = new Definition[]
    {
        new(Overview, "OrgInfo_Tab_Overview", true),
        new(Settings, "OrgInfo_Tab_Settings", true),
        new(Users, "OrgInfo_Tab_Users", true),
        new(Profiles, "OrgInfo_Tab_Profiles", true),
        new(PermissionSets, "OrgInfo_Tab_PermissionSets", true),
        new(Roles, "OrgInfo_Tab_Roles", true),
        new(Objects, "OrgInfo_Tab_Objects", true),
        new(Owds, "OrgInfo_Tab_Owds", true),
        new(ApexClasses, "OrgInfo_Tab_ApexClasses", false),
        new(ApexTriggers, "OrgInfo_Tab_ApexTriggers", false),
        new(Flows, "OrgInfo_Tab_Flows", false),
        new(ScheduledJobs, "OrgInfo_Tab_ScheduledJobs", false),
        new(ConnectedApps, "OrgInfo_Tab_ConnectedApps", false),
        new(InstalledPackages, "OrgInfo_Tab_InstalledPackages", false),
        new(LoginHistory, "OrgInfo_Tab_LoginHistory", false),
        new(SetupAuditTrail, "OrgInfo_Tab_AuditTrail", false),
        new(RecordTypes, "OrgInfo_Tab_RecordTypes", false),
        new(Currencies, "OrgInfo_Tab_Currencies", false),
    };

    /// <summary>初回オープン時に自動取得するセクション ID（オブジェクト項目は遅延取得のため含まない）。</summary>
    public static IReadOnlyList<string> DefaultSectionIds { get; } = All.Where(d => d.AutoFetch).Select(d => d.Id).ToArray();

    public static Definition? Find(string sectionId) => All.FirstOrDefault(d => d.Id == sectionId);

    private static OrgInfoColumn C(string key, string labelKey) => new(key, labelKey);

    public static IReadOnlyList<OrgInfoColumn> OverviewColumns { get; } = new[]
    {
        C("item", "OrgInfo_Col_Item"),
        C("value", "OrgInfo_Col_Value"),
    };

    public static IReadOnlyList<OrgInfoColumn> UserColumns { get; } = new[]
    {
        C("name", "OrgInfo_Col_Name"),
        C("username", "OrgInfo_Col_Username"),
        C("email", "OrgInfo_Col_Email"),
        C("active", "OrgInfo_Col_Active"),
        C("profile", "OrgInfo_Col_Profile"),
        C("role", "OrgInfo_Col_Role"),
        C("userType", "OrgInfo_Col_UserType"),
        C("lastLogin", "OrgInfo_Col_LastLogin"),
        C("created", "OrgInfo_Col_Created"),
    };

    public static IReadOnlyList<OrgInfoColumn> ProfileColumns { get; } = new[]
    {
        C("name", "OrgInfo_Col_Name"),
        C("userType", "OrgInfo_Col_UserType"),
        C("description", "OrgInfo_Col_Description"),
        C("activeUsers", "OrgInfo_Col_ActiveUsers"),
        C("created", "OrgInfo_Col_Created"),
    };

    public static IReadOnlyList<OrgInfoColumn> PermissionSetColumns { get; } = new[]
    {
        C("label", "OrgInfo_Col_Label"),
        C("apiName", "OrgInfo_Col_ApiName"),
        C("description", "OrgInfo_Col_Description"),
        C("assignedUsers", "OrgInfo_Col_AssignedUsers"),
        C("created", "OrgInfo_Col_Created"),
    };

    public static IReadOnlyList<OrgInfoColumn> RoleColumns { get; } = new[]
    {
        C("name", "OrgInfo_Col_Name"),
        C("developerName", "OrgInfo_Col_DeveloperName"),
        C("parentRole", "OrgInfo_Col_ParentRole"),
        C("description", "OrgInfo_Col_Description"),
    };

    public static IReadOnlyList<OrgInfoColumn> ObjectColumns { get; } = new[]
    {
        C("apiName", "OrgInfo_Col_ApiName"),
        C("label", "OrgInfo_Col_Label"),
        C("keyPrefix", "OrgInfo_Col_KeyPrefix"),
        C("kind", "OrgInfo_Col_Kind"),
        C("customSetting", "OrgInfo_Col_CustomSetting"),
        C("namespace", "OrgInfo_Col_Namespace"),
        C("internalOwds", "OrgInfo_Col_InternalOwds"),
        C("externalOwds", "OrgInfo_Col_ExternalOwds"),
    };

    public static IReadOnlyList<OrgInfoColumn> OwdColumns { get; } = new[]
    {
        C("target", "OrgInfo_Col_Target"),
        C("internal", "OrgInfo_Col_Internal"),
        C("external", "OrgInfo_Col_External"),
        C("source", "OrgInfo_Col_Source"),
    };

    /// <summary>オブジェクト項目（FieldDefinition）の列。</summary>
    public static IReadOnlyList<OrgInfoColumn> FieldsColumns { get; } = new[]
    {
        C("apiName", "OrgInfo_Col_ApiName"),
        C("label", "OrgInfo_Col_Label"),
        C("dataType", "OrgInfo_Col_DataType"),
        C("custom", "OrgInfo_Col_Custom"),
        C("referenceTo", "OrgInfo_Col_ReferenceTo"),
        C("nillable", "OrgInfo_Col_Nillable"),
        C("indexed", "OrgInfo_Col_Indexed"),
        C("calculated", "OrgInfo_Col_Calculated"),
        C("historyTracked", "OrgInfo_Col_HistoryTracked"),
        C("description", "OrgInfo_Col_Description"),
    };

    // ---- 追加候補セクションの列（Step 4）----

    /// <summary>主な設定（API 取得値 + リンクのみの設定行）。</summary>
    public static IReadOnlyList<OrgInfoColumn> SettingsColumns { get; } = new[]
    {
        C("item", "OrgInfo_Col_Item"),
        C("value", "OrgInfo_Col_Value"),
    };

    public static IReadOnlyList<OrgInfoColumn> ApexClassColumns { get; } = new[]
    {
        C("name", "OrgInfo_Col_Name"),
        C("apiVersion", "OrgInfo_Col_ApiVersion"),
        C("status", "OrgInfo_Col_Status"),
        C("valid", "OrgInfo_Col_Valid"),
        C("length", "OrgInfo_Col_CodeLength"),
        C("lastModified", "OrgInfo_Col_LastModified"),
    };

    public static IReadOnlyList<OrgInfoColumn> ApexTriggerColumns { get; } = new[]
    {
        C("name", "OrgInfo_Col_Name"),
        C("apiVersion", "OrgInfo_Col_ApiVersion"),
        C("status", "OrgInfo_Col_Status"),
        C("valid", "OrgInfo_Col_Valid"),
        C("lastModified", "OrgInfo_Col_LastModified"),
    };

    public static IReadOnlyList<OrgInfoColumn> FlowColumns { get; } = new[]
    {
        C("label", "OrgInfo_Col_Label"),
        C("apiName", "OrgInfo_Col_ApiName"),
        C("processType", "OrgInfo_Col_ProcessType"),
        C("active", "OrgInfo_Col_Active"),
        C("lastModified", "OrgInfo_Col_LastModified"),
    };

    public static IReadOnlyList<OrgInfoColumn> ScheduledJobColumns { get; } = new[]
    {
        C("name", "OrgInfo_Col_Name"),
        C("jobType", "OrgInfo_Col_JobType"),
        C("nextFireTime", "OrgInfo_Col_NextFireTime"),
        C("previousFireTime", "OrgInfo_Col_PreviousFireTime"),
        C("state", "OrgInfo_Col_State"),
        C("timesTriggered", "OrgInfo_Col_TimesTriggered"),
        C("created", "OrgInfo_Col_Created"),
    };

    public static IReadOnlyList<OrgInfoColumn> ConnectedAppColumns { get; } = new[]
    {
        C("name", "OrgInfo_Col_Name"),
        C("created", "OrgInfo_Col_Created"),
        C("lastModified", "OrgInfo_Col_LastModified"),
    };

    public static IReadOnlyList<OrgInfoColumn> InstalledPackageColumns { get; } = new[]
    {
        C("name", "OrgInfo_Col_Name"),
        C("namespace", "OrgInfo_Col_Namespace"),
        C("version", "OrgInfo_Col_Version"),
    };

    public static IReadOnlyList<OrgInfoColumn> LoginHistoryColumns { get; } = new[]
    {
        C("user", "OrgInfo_Col_User"),
        C("loginTime", "OrgInfo_Col_LoginTime"),
        C("loginType", "OrgInfo_Col_LoginType"),
        C("status", "OrgInfo_Col_Status"),
        C("sourceIp", "OrgInfo_Col_SourceIp"),
        C("browser", "OrgInfo_Col_Browser"),
        C("platform", "OrgInfo_Col_Platform"),
    };

    public static IReadOnlyList<OrgInfoColumn> AuditTrailColumns { get; } = new[]
    {
        C("datetime", "OrgInfo_Col_DateTime"),
        C("user", "OrgInfo_Col_User"),
        C("action", "OrgInfo_Col_Action"),
        C("section", "OrgInfo_Col_Section"),
        C("detail", "OrgInfo_Col_Detail"),
    };

    public static IReadOnlyList<OrgInfoColumn> RecordTypeColumns { get; } = new[]
    {
        C("sobject", "OrgInfo_Col_Sobject"),
        C("name", "OrgInfo_Col_Name"),
        C("developerName", "OrgInfo_Col_DeveloperName"),
        C("description", "OrgInfo_Col_Description"),
        C("created", "OrgInfo_Col_Created"),
    };

    public static IReadOnlyList<OrgInfoColumn> CurrencyColumns { get; } = new[]
    {
        C("isoCode", "OrgInfo_Col_IsoCode"),
        C("name", "OrgInfo_Col_Name"),
        C("active", "OrgInfo_Col_Active"),
        C("conversionRate", "OrgInfo_Col_ConversionRate"),
        C("created", "OrgInfo_Col_Created"),
    };

    /// <summary>セクション ID から列定義を返す（未定義は空）。</summary>
    public static IReadOnlyList<OrgInfoColumn> ColumnsFor(string sectionId) => sectionId switch
    {
        Overview => OverviewColumns,
        Settings => SettingsColumns,
        Users => UserColumns,
        Profiles => ProfileColumns,
        PermissionSets => PermissionSetColumns,
        Roles => RoleColumns,
        Objects => ObjectColumns,
        Owds => OwdColumns,
        ApexClasses => ApexClassColumns,
        ApexTriggers => ApexTriggerColumns,
        Flows => FlowColumns,
        ScheduledJobs => ScheduledJobColumns,
        ConnectedApps => ConnectedAppColumns,
        InstalledPackages => InstalledPackageColumns,
        LoginHistory => LoginHistoryColumns,
        SetupAuditTrail => AuditTrailColumns,
        RecordTypes => RecordTypeColumns,
        Currencies => CurrencyColumns,
        _ when IsFieldsSection(sectionId) => FieldsColumns,
        _ => Array.Empty<OrgInfoColumn>(),
    };

    /// <summary>概要セクションの項目定義（行 ID とラベルキーの対応）。</summary>
    public sealed record OverviewItem(string Id, string LabelKey);

    public static IReadOnlyList<OverviewItem> OverviewItems { get; } = new OverviewItem[]
    {
        new("orgName", "OrgInfo_Item_OrgName"),
        new("orgId", "OrgInfo_Item_OrgId"),
        new("edition", "OrgInfo_Item_Edition"),
        new("environment", "OrgInfo_Item_Environment"),
        new("instance", "OrgInfo_Item_Instance"),
        new("username", "OrgInfo_Item_Username"),
        new("connection", "OrgInfo_Item_Connection"),
        new("apiVersion", "OrgInfo_Item_ApiVersion"),
        new("instanceUrl", "OrgInfo_Item_InstanceUrl"),
        new("language", "OrgInfo_Item_Language"),
        new("locale", "OrgInfo_Item_Locale"),
        new("timeZone", "OrgInfo_Item_TimeZone"),
        new("fiscalYearStart", "OrgInfo_Item_FiscalYearStart"),
        new("namespace", "OrgInfo_Item_Namespace"),
        new("division", "OrgInfo_Item_Division"),
        new("phone", "OrgInfo_Item_Phone"),
        new("address", "OrgInfo_Item_Address"),
        new("dataStorage", "OrgInfo_Item_DataStorage"),
        new("fileStorage", "OrgInfo_Item_FileStorage"),
        new("dailyApiRequests", "OrgInfo_Item_DailyApiRequests"),
    };

    public static string? OverviewLabelKey(string itemId) =>
        OverviewItems.FirstOrDefault(i => i.Id == itemId)?.LabelKey;

    /// <summary>OWD の組織既定アクセス（Organization の Default*Access フィールドとの対応）。</summary>
    public sealed record OwdTarget(string Id, string LabelKey, string OrganizationField);

    public static IReadOnlyList<OwdTarget> OwdDefaultTargets { get; } = new OwdTarget[]
    {
        new("Account", "OrgInfo_Target_Account", "DefaultAccountAccess"),
        new("Contact", "OrgInfo_Target_Contact", "DefaultContactAccess"),
        new("Opportunity", "OrgInfo_Target_Opportunity", "DefaultOpportunityAccess"),
        new("Lead", "OrgInfo_Target_Lead", "DefaultLeadAccess"),
        new("Case", "OrgInfo_Target_Case", "DefaultCaseAccess"),
        new("Pricebook", "OrgInfo_Target_Pricebook", "DefaultPricebookAccess"),
        new("Calendar", "OrgInfo_Target_Calendar", "DefaultCalendarAccess"),
        new("Campaign", "OrgInfo_Target_Campaign", "DefaultCampaignAccess"),
    };

    public static string? OwdTargetLabelKey(string targetId) =>
        OwdDefaultTargets.FirstOrDefault(t => t.Id == targetId)?.LabelKey;

    /// <summary>「主な設定」セクションの項目定义（Id / ラベルキー / Organization フィールド / リンクの行か）。</summary>
    public sealed record SettingsItem(string Id, string LabelKey, string? OrganizationField, bool IsLink);

    public static IReadOnlyList<SettingsItem> SettingsItems { get; } = new SettingsItem[]
    {
        new("lightningLogin", "OrgInfo_Settings_LightningLogin", "PreferencesLightningLoginEnabled", false),
        new("onlyLightningLoginPerms", "OrgInfo_Settings_OnlyLlpUsers", "PreferencesOnlyLLPermUserAllowed", false),
        new("transactionSecurity", "OrgInfo_Settings_TransactionSecurity", "PreferencesTransactionSecurityPolicy", false),
        new("consentManagement", "OrgInfo_Settings_ConsentManagement", "PreferencesConsentManagementEnabled", false),
        new("requireOppProducts", "OrgInfo_Settings_RequireOppProducts", "PreferencesRequireOpportunityProducts", false),
        new("autoSelectIndividual", "OrgInfo_Settings_AutoSelectIndividual", "PreferencesAutoSelectIndividualOnMerge", false),
        new("receivesInfoEmails", "OrgInfo_Settings_ReceivesInfoEmails", "ReceivesInfoEmails", false),
        new("receivesAdminEmails", "OrgInfo_Settings_ReceivesAdminEmails", "ReceivesAdminInfoEmails", false),
        new("complianceBccEmail", "OrgInfo_Settings_ComplianceBcc", "ComplianceBccEmail", false),
        new("uiSkin", "OrgInfo_Settings_UiSkin", "UiSkin", false),
        new("webToCaseOrigin", "OrgInfo_Settings_WebToCaseOrigin", "WebToCaseDefaultOrigin", false),
        new("trialExpiration", "OrgInfo_Settings_TrialExpiration", "TrialExpirationDate", false),
        new("isReadOnly", "OrgInfo_Settings_IsReadOnly", "IsReadOnly", false),
        new("companyInfo", "OrgInfo_Settings_CompanyInfo", null, true),
        new("sessionSettings", "OrgInfo_Settings_SessionSettings", null, true),
        new("passwordPolicies", "OrgInfo_Settings_PasswordPolicies", null, true),
        new("loginIpRanges", "OrgInfo_Settings_LoginIpRanges", null, true),
        new("resourceUsage", "OrgInfo_Settings_ResourceUsage", null, true),
        new("loginHistory", "OrgInfo_Settings_LoginHistory", null, true),
        new("auditTrail", "OrgInfo_Settings_AuditTrail", null, true),
    };

    public static string? SettingsLabelKey(string itemId) =>
        SettingsItems.FirstOrDefault(i => i.Id == itemId)?.LabelKey;
}

/// <summary>キャッシュに保存する言語非依存の値トークン（表示時にローカライズする）。</summary>
public static class OrgInfoTokens
{
    public const string True = "true";
    public const string False = "false";
    public const string Standard = "standard";
    public const string Custom = "custom";
    public const string OrgDefault = "org";
    public const string Object = "object";
    public const string Sandbox = "sandbox";
    public const string Production = "production";

    /// <summary>「主な設定」のリンクのみ行（値 = リンクのみ）。</summary>
    public const string LinkOnly = "linkonly";
}
