namespace SfUi.Core;

/// <summary>
/// 組織情報セクションの SOQL を組み立てる（純粋関数・単体テスト対象）。
/// フィールドは Salesforce Object Reference / Tooling API の仕様に基づく（実装時に describe で存在確認）。
/// </summary>
public static class OrgInfoQueryBuilder
{
    public static string BuildOrganizationQuery() =>
        "SELECT Id, Name, Division, OrganizationType, InstanceName, IsSandbox, LanguageLocaleKey, DefaultLocaleSidKey, "
        + "TimeZoneSidKey, FiscalYearStartMonth, NamespacePrefix, Phone, Street, City, State, PostalCode, Country "
        + "FROM Organization";

    public static string BuildOrganizationOwdsQuery() =>
        "SELECT DefaultAccountAccess, DefaultContactAccess, DefaultOpportunityAccess, DefaultLeadAccess, DefaultCaseAccess, "
        + "DefaultPricebookAccess, DefaultCalendarAccess, DefaultCampaignAccess "
        + "FROM Organization";

    public static string BuildUserQuery() =>
        "SELECT Id, Name, Username, Email, IsActive, UserType, LastLoginDate, CreatedDate, Profile.Name, UserRole.Name "
        + "FROM User ORDER BY IsActive DESC, Name";

    public static string BuildProfileQuery() =>
        "SELECT Id, Name, UserType, Description, CreatedDate FROM Profile ORDER BY Name";

    public static string BuildProfileUserCountQuery() =>
        "SELECT ProfileId, COUNT(Id) cnt FROM User WHERE IsActive = true GROUP BY ProfileId";

    public static string BuildPermissionSetQuery() =>
        "SELECT Id, Name, Label, IsOwnedByProfile, ProfileId, Description, CreatedDate "
        + "FROM PermissionSet WHERE IsOwnedByProfile = false ORDER BY Label";

    public static string BuildPermissionSetAssignmentCountQuery() =>
        "SELECT PermissionSetId, COUNT(Id) cnt FROM PermissionSetAssignment GROUP BY PermissionSetId";

    public static string BuildUserRoleQuery() =>
        "SELECT Id, Name, DeveloperName, ParentRoleId, RollupDescription FROM UserRole ORDER BY Name";

    public static string BuildEntityDefinitionQuery() =>
        "SELECT QualifiedApiName, Label, KeyPrefix, IsCustomizable, IsCustomSetting, NamespacePrefix, "
        + "InternalSharingModel, ExternalSharingModel "
        + "FROM EntityDefinition WHERE IsCustomizable = true AND IsDeprecatedAndHidden = false ORDER BY QualifiedApiName";

    /// <summary>オブジェクト項目（FieldDefinition）。WHERE に EntityDefinition.QualifiedApiName が必須。</summary>
    public static string BuildFieldDefinitionQuery(string objectApiName) =>
        "SELECT QualifiedApiName, Label, DataType, IsNillable, IsIndexed, IsCalculated, IsFieldHistoryTracked, "
        + "RelationshipName, ReferenceTo, Description "
        + $"FROM FieldDefinition WHERE EntityDefinition.QualifiedApiName = '{EscapeSoqlString(objectApiName)}' ORDER BY Label";

    // ---- 主な設定（Step 4・describe で存在確認済みのフィールドのみ使用）----

    public static string BuildSettingsQuery() =>
        "SELECT Id, PreferencesLightningLoginEnabled, PreferencesOnlyLLPermUserAllowed, "
        + "PreferencesTransactionSecurityPolicy, PreferencesConsentManagementEnabled, "
        + "PreferencesRequireOpportunityProducts, PreferencesAutoSelectIndividualOnMerge, "
        + "ReceivesInfoEmails, ReceivesAdminInfoEmails, ComplianceBccEmail, UiSkin, WebToCaseDefaultOrigin, "
        + "TrialExpirationDate, IsReadOnly "
        + "FROM Organization";

    // ---- 追加候補セクション（Step 4）----

    public static string BuildApexClassQuery() =>
        "SELECT Id, Name, ApiVersion, Status, IsValid, LengthWithoutComments, LastModifiedDate "
        + "FROM ApexClass ORDER BY Name";

    public static string BuildApexTriggerQuery() =>
        "SELECT Id, Name, ApiVersion, Status, IsValid, LastModifiedDate FROM ApexTrigger ORDER BY Name";

    public static string BuildFlowQuery() =>
        "SELECT Id, Label, ApiName, ProcessType, IsActive, LastModifiedDate FROM FlowDefinitionView ORDER BY Label";

    public static string BuildScheduledJobQuery() =>
        "SELECT Id, CronJobDetail.Name, CronJobDetail.JobType, NextFireTime, PreviousFireTime, State, TimesTriggered, CreatedDate "
        + "FROM CronTrigger WHERE State != 'DELETED' ORDER BY NextFireTime";

    public static string BuildConnectedAppQuery() =>
        "SELECT Id, Name, CreatedDate, LastModifiedDate FROM ConnectedApplication ORDER BY Name";

    /// <summary>インストール済みパッケージ（Tooling API）。</summary>
    public static string BuildInstalledPackageQuery() =>
        "SELECT Id, SubscriberPackageId, SubscriberPackage.Name, SubscriberPackage.NamespacePrefix, SubscriberPackageVersionId "
        + "FROM InstalledSubscriberPackage";

    /// <summary>パッケージバージョン（Tooling API）。Id = '...' の単一形式のみ許可される。</summary>
    public static string BuildPackageVersionQuery(string versionId) =>
        $"SELECT Id, MajorVersion, MinorVersion, PatchVersion, BuildNumber FROM SubscriberPackageVersion WHERE Id = '{EscapeSoqlString(versionId)}'";

    /// <summary>ログイン履歴（直近 200 件）。</summary>
    public static string BuildLoginHistoryQuery() =>
        "SELECT Id, UserId, LoginTime, LoginType, Status, SourceIp, Browser, Platform, Application "
        + "FROM LoginHistory ORDER BY LoginTime DESC LIMIT 200";

    /// <summary>設定変更履歴（直近 200 件）。</summary>
    public static string BuildAuditTrailQuery() =>
        "SELECT Id, Action, Section, CreatedDate, CreatedBy.Name, Display "
        + "FROM SetupAuditTrail ORDER BY CreatedDate DESC LIMIT 200";

    public static string BuildRecordTypeQuery() =>
        "SELECT Id, Name, DeveloperName, SobjectType, IsActive, Description, CreatedDate "
        + "FROM RecordType WHERE IsActive = true ORDER BY SobjectType, Name";

    /// <summary>通貨（多通貨が無効な組織では sObject 自体が未サポートでエラーになる）。</summary>
    public static string BuildCurrencyQuery() =>
        "SELECT Id, IsoCode, Name, IsActive, ConversionRate, CreatedDate FROM CurrencyType ORDER BY IsoCode";

    /// <summary>SOQL 文字列リテラルをエスケープする（\\ → \\\\、' → \\'）。</summary>
    public static string EscapeSoqlString(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
}
