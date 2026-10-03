using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// 組織情報セクションの取得（REST / Tooling API）。取得結果は OrgInfoSection として返し、
/// 永続化は OrgInfoCacheStore が行う。初回のみ自動取得・以降は手動再取得という方針は呼び出し側が制御する。
/// </summary>
public sealed class OrgInfoService
{
    /// <summary>1 セクションで保持する最大行数（ページングの安全弁）。</summary>
    public const int MaxRowsPerSection = 10000;

    /// <summary>パッケージバージョンの個別照会の上限（Tooling API は Id = '...' の単一形式のみ許可）。</summary>
    public const int MaxPackageVersionLookups = 20;

    private const int MaxPages = 200;

    private readonly SalesforceRestClient _rest;
    private readonly OrgService _orgs;
    private readonly OrgInfoCacheStore _cache;
    private readonly AppLog _log;

    public OrgInfoService(SalesforceRestClient rest, OrgService orgs, OrgInfoCacheStore cache, AppLog log)
    {
        _rest = rest;
        _orgs = orgs;
        _cache = cache;
        _log = log;
    }

    /// <summary>セクション ID を指定して取得する。</summary>
    public Task<OrgInfoSection> FetchSectionAsync(OrgInfo org, string sectionId, CancellationToken cancellationToken = default)
        => sectionId switch
        {
            OrgInfoSections.Overview => FetchOverviewAsync(org, cancellationToken),
            OrgInfoSections.Settings => FetchSettingsAsync(org, cancellationToken),
            OrgInfoSections.Users => FetchUsersAsync(org, cancellationToken),
            OrgInfoSections.Profiles => FetchProfilesAsync(org, cancellationToken),
            OrgInfoSections.PermissionSets => FetchPermissionSetsAsync(org, cancellationToken),
            OrgInfoSections.Roles => FetchRolesAsync(org, cancellationToken),
            OrgInfoSections.Objects => FetchObjectsAsync(org, cancellationToken),
            OrgInfoSections.Owds => FetchOwdsAsync(org, cancellationToken),
            OrgInfoSections.ApexClasses => FetchApexClassesAsync(org, cancellationToken),
            OrgInfoSections.ApexTriggers => FetchApexTriggersAsync(org, cancellationToken),
            OrgInfoSections.Flows => FetchFlowsAsync(org, cancellationToken),
            OrgInfoSections.ScheduledJobs => FetchScheduledJobsAsync(org, cancellationToken),
            OrgInfoSections.ConnectedApps => FetchConnectedAppsAsync(org, cancellationToken),
            OrgInfoSections.InstalledPackages => FetchInstalledPackagesAsync(org, cancellationToken),
            OrgInfoSections.LoginHistory => FetchLoginHistoryAsync(org, cancellationToken),
            OrgInfoSections.SetupAuditTrail => FetchAuditTrailAsync(org, cancellationToken),
            OrgInfoSections.RecordTypes => FetchRecordTypesAsync(org, cancellationToken),
            OrgInfoSections.Currencies => FetchCurrenciesAsync(org, cancellationToken),
            _ when OrgInfoSections.IsFieldsSection(sectionId) => FetchFieldsAsync(org, FieldsApiName(sectionId), cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(sectionId), sectionId, "未対応の組織情報セクションです"),
        };

    /// <summary>概要（組織情報 + API 利用状況）を取得する。</summary>
    public async Task<OrgInfoSection> FetchOverviewAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var target = TargetOrg(org);
        var stopwatch = Stopwatch.StartNew();
        var auth = await _orgs.GetAuthAsync(target, cancellationToken: cancellationToken).ConfigureAwait(false);

        JsonElement organization;
        using (var document = await _rest.QueryAsync(target, OrgInfoQueryBuilder.BuildOrganizationQuery(), useToolingApi: false, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            organization = FirstRecord(document.RootElement) ?? default;
        }

        if (organization.ValueKind != JsonValueKind.Object)
        {
            throw new SalesforceApiException("Organization レコードを取得できませんでした。");
        }

        JsonElement limits;
        using (var document = await _rest.GetLimitsAsync(target, cancellationToken).ConfigureAwait(false))
        {
            limits = document.RootElement.Clone();
        }

        var rows = ParseOverviewRows(org, auth, organization, limits);
        return OrgInfoSection.Create(OrgInfoSections.Overview, OrgInfoSections.OverviewColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>ユーザー一覧を取得する（全ページ）。</summary>
    public async Task<OrgInfoSection> FetchUsersAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var target = TargetOrg(org);
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(target, OrgInfoQueryBuilder.BuildUserQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var rows = ParseUserRows(records, InstanceUrl(org));
        return OrgInfoSection.Create(OrgInfoSections.Users, OrgInfoSections.UserColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>プロファイル一覧（有効ユーザー数の集計つき）を取得する。</summary>
    public async Task<OrgInfoSection> FetchProfilesAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var target = TargetOrg(org);
        var stopwatch = Stopwatch.StartNew();
        var profileRecords = await QueryAllRecordsAsync(target, OrgInfoQueryBuilder.BuildProfileQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var countRecords = await QueryAllRecordsAsync(target, OrgInfoQueryBuilder.BuildProfileUserCountQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var rows = ParseProfileRows(profileRecords, ParseProfileUserCounts(countRecords), InstanceUrl(org));
        return OrgInfoSection.Create(OrgInfoSections.Profiles, OrgInfoSections.ProfileColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>権限セット一覧（割当ユーザー数の集計つき）を取得する（プロファイル由来は除外）。</summary>
    public async Task<OrgInfoSection> FetchPermissionSetsAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var target = TargetOrg(org);
        var stopwatch = Stopwatch.StartNew();
        var permissionSetRecords = await QueryAllRecordsAsync(target, OrgInfoQueryBuilder.BuildPermissionSetQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var countRecords = await QueryAllRecordsAsync(target, OrgInfoQueryBuilder.BuildPermissionSetAssignmentCountQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var rows = ParsePermissionSetRows(permissionSetRecords, ParsePermissionSetAssignmentCounts(countRecords));
        return OrgInfoSection.Create(OrgInfoSections.PermissionSets, OrgInfoSections.PermissionSetColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>ロール一覧を取得する（親ロール名はロール一覧内で解決）。</summary>
    public async Task<OrgInfoSection> FetchRolesAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var target = TargetOrg(org);
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(target, OrgInfoQueryBuilder.BuildUserRoleQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var rows = ParseRoleRows(records);
        return OrgInfoSection.Create(OrgInfoSections.Roles, OrgInfoSections.RoleColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>オブジェクト一覧（EntityDefinition / Tooling API）を取得する。</summary>
    public async Task<OrgInfoSection> FetchObjectsAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var target = TargetOrg(org);
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(target, OrgInfoQueryBuilder.BuildEntityDefinitionQuery(), useToolingApi: true, cancellationToken).ConfigureAwait(false);
        var rows = ParseObjectRows(records, InstanceUrl(org));
        return OrgInfoSection.Create(OrgInfoSections.Objects, OrgInfoSections.ObjectColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>OWD（組織の既定アクセス + オブジェクト別共有モデル）を取得する。</summary>
    public async Task<OrgInfoSection> FetchOwdsAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var target = TargetOrg(org);
        var stopwatch = Stopwatch.StartNew();

        JsonElement organization;
        using (var document = await _rest.QueryAsync(target, OrgInfoQueryBuilder.BuildOrganizationOwdsQuery(), useToolingApi: false, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            organization = FirstRecord(document.RootElement) ?? default;
        }

        var objectRecords = await QueryAllRecordsAsync(target, OrgInfoQueryBuilder.BuildEntityDefinitionQuery(), useToolingApi: true, cancellationToken).ConfigureAwait(false);
        var rows = ParseOwdRows(organization, objectRecords);
        return OrgInfoSection.Create(OrgInfoSections.Owds, OrgInfoSections.OwdColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>オブジェクト項目（FieldDefinition / Tooling API）を取得する。</summary>
    public async Task<OrgInfoSection> FetchFieldsAsync(OrgInfo org, string objectApiName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectApiName))
        {
            throw new ArgumentException("objectApiName is required.", nameof(objectApiName));
        }

        var target = TargetOrg(org);
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(target, OrgInfoQueryBuilder.BuildFieldDefinitionQuery(objectApiName), useToolingApi: true, cancellationToken).ConfigureAwait(false);
        var rows = ParseFieldRows(records);
        return OrgInfoSection.Create(OrgInfoSections.Fields(objectApiName), OrgInfoSections.FieldsColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>主な設定（API 取得できる設定値 + リンクのみの設定行）を取得する。</summary>
    public async Task<OrgInfoSection> FetchSettingsAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var target = TargetOrg(org);
        var stopwatch = Stopwatch.StartNew();

        JsonElement organization;
        using (var document = await _rest.QueryAsync(target, OrgInfoQueryBuilder.BuildSettingsQuery(), useToolingApi: false, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            organization = FirstRecord(document.RootElement) ?? default;
        }

        var rows = ParseSettingsRows(organization, InstanceUrl(org));
        return OrgInfoSection.Create(OrgInfoSections.Settings, OrgInfoSections.SettingsColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Apex クラス一覧を取得する。</summary>
    public async Task<OrgInfoSection> FetchApexClassesAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(TargetOrg(org), OrgInfoQueryBuilder.BuildApexClassQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var rows = ParseApexClassRows(records);
        return OrgInfoSection.Create(OrgInfoSections.ApexClasses, OrgInfoSections.ApexClassColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Apex トリガ一覧を取得する。</summary>
    public async Task<OrgInfoSection> FetchApexTriggersAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(TargetOrg(org), OrgInfoQueryBuilder.BuildApexTriggerQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var rows = ParseApexTriggerRows(records);
        return OrgInfoSection.Create(OrgInfoSections.ApexTriggers, OrgInfoSections.ApexTriggerColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>フロー一覧（FlowDefinitionView）を取得する。</summary>
    public async Task<OrgInfoSection> FetchFlowsAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(TargetOrg(org), OrgInfoQueryBuilder.BuildFlowQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var rows = ParseFlowRows(records);
        return OrgInfoSection.Create(OrgInfoSections.Flows, OrgInfoSections.FlowColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>スケジュール済みジョブ（CronTrigger）を取得する。</summary>
    public async Task<OrgInfoSection> FetchScheduledJobsAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(TargetOrg(org), OrgInfoQueryBuilder.BuildScheduledJobQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var rows = ParseScheduledJobRows(records);
        return OrgInfoSection.Create(OrgInfoSections.ScheduledJobs, OrgInfoSections.ScheduledJobColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>接続アプリ一覧を取得する。</summary>
    public async Task<OrgInfoSection> FetchConnectedAppsAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(TargetOrg(org), OrgInfoQueryBuilder.BuildConnectedAppQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var rows = ParseConnectedAppRows(records);
        return OrgInfoSection.Create(OrgInfoSections.ConnectedApps, OrgInfoSections.ConnectedAppColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>インストール済みパッケージ（Tooling API）+ バージョン情報を取得する。</summary>
    public async Task<OrgInfoSection> FetchInstalledPackagesAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var target = TargetOrg(org);
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(target, OrgInfoQueryBuilder.BuildInstalledPackageQuery(), useToolingApi: true, cancellationToken).ConfigureAwait(false);

        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var versionIds = records
            .Select(r => GetString(r, "SubscriberPackageVersionId"))
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxPackageVersionLookups)
            .ToList();
        foreach (var versionId in versionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var document = await _rest.QueryAsync(target, OrgInfoQueryBuilder.BuildPackageVersionQuery(versionId), useToolingApi: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                var record = FirstRecord(document.RootElement);
                if (record is { ValueKind: JsonValueKind.Object } rec)
                {
                    versions[versionId] = FormatPackageVersion(rec) ?? versionId;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warn($"パッケージバージョンの取得に失敗: {versionId}: {ex.Message}");
            }
        }

        var rows = ParseInstalledPackageRows(records, versions);
        return OrgInfoSection.Create(OrgInfoSections.InstalledPackages, OrgInfoSections.InstalledPackageColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>ログイン履歴（直近 200 件）を取得する。ユーザー名はユーザ一覧キャッシュから解決する。</summary>
    public async Task<OrgInfoSection> FetchLoginHistoryAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(TargetOrg(org), OrgInfoQueryBuilder.BuildLoginHistoryQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var rows = ParseLoginHistoryRows(records, LoadUserNames(OrgInfoCacheStore.GetOrgKey(org)));
        return OrgInfoSection.Create(OrgInfoSections.LoginHistory, OrgInfoSections.LoginHistoryColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>設定変更履歴（SetupAuditTrail 直近 200 件）を取得する。</summary>
    public async Task<OrgInfoSection> FetchAuditTrailAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(TargetOrg(org), OrgInfoQueryBuilder.BuildAuditTrailQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var rows = ParseAuditTrailRows(records);
        return OrgInfoSection.Create(OrgInfoSections.SetupAuditTrail, OrgInfoSections.AuditTrailColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>レコードタイプ一覧を取得する。</summary>
    public async Task<OrgInfoSection> FetchRecordTypesAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var records = await QueryAllRecordsAsync(TargetOrg(org), OrgInfoQueryBuilder.BuildRecordTypeQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        var rows = ParseRecordTypeRows(records);
        return OrgInfoSection.Create(OrgInfoSections.RecordTypes, OrgInfoSections.RecordTypeColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>通貨一覧を取得する（多通貨が無効な組織では未サポートのため専用メッセージを返す）。</summary>
    public async Task<OrgInfoSection> FetchCurrenciesAsync(OrgInfo org, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        List<JsonElement> records;
        try
        {
            records = await QueryAllRecordsAsync(TargetOrg(org), OrgInfoQueryBuilder.BuildCurrencyQuery(), useToolingApi: false, cancellationToken).ConfigureAwait(false);
        }
        catch (SalesforceApiException ex) when (IsUnsupportedObjectError(ex))
        {
            throw new SalesforceApiException(UiText.T("OrgInfo_Currencies_Unsupported"));
        }

        var rows = ParseCurrencyRows(records);
        return OrgInfoSection.Create(OrgInfoSections.Currencies, OrgInfoSections.CurrencyColumns, rows, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }

    // ---- パース（純粋関数・単体テスト対象）----

    public static List<OrgInfoRow> ParseOverviewRows(OrgInfo org, OrgAuthInfo auth, JsonElement organization, JsonElement limits)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["orgName"] = GetString(organization, "Name"),
            ["orgId"] = org.OrgId ?? GetString(organization, "Id"),
            ["edition"] = GetString(organization, "OrganizationType"),
            ["environment"] = GetBool(organization, "IsSandbox") switch
            {
                true => OrgInfoTokens.Sandbox,
                false => OrgInfoTokens.Production,
                _ => null,
            },
            ["instance"] = GetString(organization, "InstanceName"),
            ["username"] = org.Username,
            ["connection"] = org.ConnectedStatus,
            ["apiVersion"] = auth.ApiVersion,
            ["instanceUrl"] = auth.InstanceUrl ?? org.InstanceUrl,
            ["language"] = GetString(organization, "LanguageLocaleKey"),
            ["locale"] = GetString(organization, "DefaultLocaleSidKey"),
            ["timeZone"] = GetString(organization, "TimeZoneSidKey"),
            ["fiscalYearStart"] = GetNumber(organization, "FiscalYearStartMonth"),
            ["namespace"] = GetString(organization, "NamespacePrefix"),
            ["division"] = GetString(organization, "Division"),
            ["phone"] = GetString(organization, "Phone"),
            ["address"] = BuildAddress(organization),
            ["dataStorage"] = FormatLimit(limits, "DataStorageMB", "MB"),
            ["fileStorage"] = FormatLimit(limits, "FileStorageMB", "MB"),
            ["dailyApiRequests"] = FormatLimit(limits, "DailyApiRequests", null),
        };

        var rows = new List<OrgInfoRow>(OrgInfoSections.OverviewItems.Count);
        foreach (var item in OrgInfoSections.OverviewItems)
        {
            values.TryGetValue(item.Id, out var value);
            // item ラベルは言語依存のためキャッシュに書かず、表示時に OverviewItems の LabelKey から解決する
            var row = new OrgInfoRow { Id = item.Id, Summary = value ?? item.Id };
            row.Cells["item"] = null;
            row.Cells["value"] = value;
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseUserRows(IEnumerable<JsonElement> records, string? instanceUrl)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var name = GetString(record, "Name");
            var username = GetString(record, "Username");
            var row = new OrgInfoRow
            {
                Id = id,
                Summary = name ?? username ?? id,
                Link = UserLink(instanceUrl, id),
            };
            row.Cells["name"] = name;
            row.Cells["username"] = username;
            row.Cells["email"] = GetString(record, "Email");
            row.Cells["active"] = FormatBool(record, "IsActive");
            row.Cells["profile"] = GetNestedString(record, "Profile", "Name");
            row.Cells["role"] = GetNestedString(record, "UserRole", "Name");
            row.Cells["userType"] = GetString(record, "UserType");
            row.Cells["lastLogin"] = GetString(record, "LastLoginDate");
            row.Cells["created"] = GetString(record, "CreatedDate");
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>プロファイル別の有効ユーザー数（COUNT 集計の結果）を読む。</summary>
    public static Dictionary<string, int> ParseProfileUserCounts(IEnumerable<JsonElement> records)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            var profileId = GetString(record, "ProfileId");
            if (!string.IsNullOrEmpty(profileId))
            {
                counts[profileId] = GetInt(record, "cnt") ?? GetInt(record, "expr0") ?? 0;
            }
        }

        return counts;
    }

    public static List<OrgInfoRow> ParseProfileRows(IEnumerable<JsonElement> records, IReadOnlyDictionary<string, int> activeUserCounts, string? instanceUrl)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var name = GetString(record, "Name");
            var row = new OrgInfoRow
            {
                Id = id,
                Summary = name ?? id,
                Link = ProfileLink(instanceUrl, id),
            };
            row.Cells["name"] = name;
            row.Cells["userType"] = GetString(record, "UserType");
            row.Cells["description"] = GetString(record, "Description");
            row.Cells["activeUsers"] = activeUserCounts.TryGetValue(id, out var count) ? count.ToString(CultureInfo.InvariantCulture) : null;
            row.Cells["created"] = GetString(record, "CreatedDate");
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>権限セット別の割当ユーザー数（COUNT 集計の結果）を読む。</summary>
    public static Dictionary<string, int> ParsePermissionSetAssignmentCounts(IEnumerable<JsonElement> records)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            var permissionSetId = GetString(record, "PermissionSetId");
            if (!string.IsNullOrEmpty(permissionSetId))
            {
                counts[permissionSetId] = GetInt(record, "cnt") ?? GetInt(record, "expr0") ?? 0;
            }
        }

        return counts;
    }

    public static List<OrgInfoRow> ParsePermissionSetRows(IEnumerable<JsonElement> records, IReadOnlyDictionary<string, int> assignedUserCounts)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var label = GetString(record, "Label");
            var name = GetString(record, "Name");
            var row = new OrgInfoRow { Id = id, Summary = label ?? name ?? id };
            row.Cells["label"] = label;
            row.Cells["apiName"] = name;
            row.Cells["description"] = GetString(record, "Description");
            row.Cells["assignedUsers"] = assignedUserCounts.TryGetValue(id, out var count) ? count.ToString(CultureInfo.InvariantCulture) : null;
            row.Cells["created"] = GetString(record, "CreatedDate");
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseRoleRows(IEnumerable<JsonElement> records)
    {
        var list = records.ToList();

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in list)
        {
            var id = GetString(record, "Id");
            var name = GetString(record, "Name");
            if (!string.IsNullOrEmpty(id) && name is not null)
            {
                names[id] = name;
            }
        }

        var rows = new List<OrgInfoRow>();
        foreach (var record in list)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var name = GetString(record, "Name");
            var parentId = GetString(record, "ParentRoleId");
            var row = new OrgInfoRow { Id = id, Summary = name ?? id };
            row.Cells["name"] = name;
            row.Cells["developerName"] = GetString(record, "DeveloperName");
            row.Cells["parentRole"] = parentId is null
                ? null
                : names.TryGetValue(parentId, out var parentName) ? parentName : parentId;
            row.Cells["description"] = GetString(record, "RollupDescription");
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseObjectRows(IEnumerable<JsonElement> records, string? instanceUrl)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var apiName = GetString(record, "QualifiedApiName") ?? string.Empty;
            var label = GetString(record, "Label");
            var row = new OrgInfoRow
            {
                Id = apiName,
                Summary = label ?? apiName,
                Link = ObjectLink(instanceUrl, apiName),
            };
            row.Cells["apiName"] = apiName;
            row.Cells["label"] = label;
            row.Cells["keyPrefix"] = GetString(record, "KeyPrefix");
            row.Cells["kind"] = apiName.Contains("__", StringComparison.Ordinal) ? OrgInfoTokens.Custom : OrgInfoTokens.Standard;
            row.Cells["customSetting"] = FormatBool(record, "IsCustomSetting");
            row.Cells["namespace"] = GetString(record, "NamespacePrefix");
            row.Cells["internalOwds"] = GetString(record, "InternalSharingModel");
            row.Cells["externalOwds"] = GetString(record, "ExternalSharingModel");
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>オブジェクト項目（FieldDefinition）の行を組み立てる。</summary>
    public static List<OrgInfoRow> ParseFieldRows(IEnumerable<JsonElement> records)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var apiName = GetString(record, "QualifiedApiName") ?? string.Empty;
            var label = GetString(record, "Label");
            var row = new OrgInfoRow { Id = apiName, Summary = label ?? apiName };
            row.Cells["apiName"] = apiName;
            row.Cells["label"] = label;
            row.Cells["dataType"] = GetString(record, "DataType");
            // FieldDefinition に IsCustom 列は無いため、API 名（__c / __mdt 等）から判定する
            row.Cells["custom"] = apiName.Contains("__", StringComparison.Ordinal) ? OrgInfoTokens.True : OrgInfoTokens.False;
            row.Cells["referenceTo"] = GetJoinedStringArray(record, "ReferenceTo");
            row.Cells["nillable"] = FormatBool(record, "IsNillable");
            row.Cells["indexed"] = FormatBool(record, "IsIndexed");
            row.Cells["calculated"] = FormatBool(record, "IsCalculated");
            row.Cells["historyTracked"] = FormatBool(record, "IsFieldHistoryTracked");
            row.Cells["description"] = GetString(record, "Description");
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseOwdRows(JsonElement organization, IEnumerable<JsonElement> entityDefinitions)
    {
        var rows = new List<OrgInfoRow>();

        if (organization.ValueKind == JsonValueKind.Object)
        {
            foreach (var target in OrgInfoSections.OwdDefaultTargets)
            {
                var row = new OrgInfoRow { Id = "org:" + target.Id, Summary = target.Id };
                row.Cells["target"] = target.Id;
                row.Cells["internal"] = GetString(organization, target.OrganizationField);
                row.Cells["external"] = null;
                row.Cells["source"] = OrgInfoTokens.OrgDefault;
                rows.Add(row);
            }
        }

        foreach (var record in entityDefinitions)
        {
            var apiName = GetString(record, "QualifiedApiName") ?? string.Empty;
            var row = new OrgInfoRow { Id = apiName, Summary = apiName };
            row.Cells["target"] = apiName;
            row.Cells["internal"] = GetString(record, "InternalSharingModel");
            row.Cells["external"] = GetString(record, "ExternalSharingModel");
            row.Cells["source"] = OrgInfoTokens.Object;
            rows.Add(row);
        }

        return rows;
    }

    // ---- 主な設定・追加候補セクションのパース（Step 4）----

    public static List<OrgInfoRow> ParseSettingsRows(JsonElement organization, string? instanceUrl)
    {
        var rows = new List<OrgInfoRow>(OrgInfoSections.SettingsItems.Count);
        foreach (var item in OrgInfoSections.SettingsItems)
        {
            string? value;
            string? link = null;
            if (item.IsLink)
            {
                value = OrgInfoTokens.LinkOnly;
                link = SettingsLink(instanceUrl, item.Id);
            }
            else
            {
                value = organization.ValueKind == JsonValueKind.Object
                    ? FormatSettingValue(organization, item.OrganizationField!)
                    : null;
            }

            // item ラベルは言語依存のためキャッシュに書かず、表示時に SettingsItems の LabelKey から解決する
            var row = new OrgInfoRow { Id = item.Id, Summary = item.Id, Link = link };
            row.Cells["item"] = null;
            row.Cells["value"] = value;
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseApexClassRows(IEnumerable<JsonElement> records)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var name = GetString(record, "Name");
            var row = new OrgInfoRow { Id = id, Summary = name ?? id };
            row.Cells["name"] = name;
            row.Cells["apiVersion"] = GetNumber(record, "ApiVersion");
            row.Cells["status"] = GetString(record, "Status");
            row.Cells["valid"] = FormatBool(record, "IsValid");
            row.Cells["length"] = GetNumber(record, "LengthWithoutComments");
            row.Cells["lastModified"] = GetString(record, "LastModifiedDate");
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseApexTriggerRows(IEnumerable<JsonElement> records)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var name = GetString(record, "Name");
            var row = new OrgInfoRow { Id = id, Summary = name ?? id };
            row.Cells["name"] = name;
            row.Cells["apiVersion"] = GetNumber(record, "ApiVersion");
            row.Cells["status"] = GetString(record, "Status");
            row.Cells["valid"] = FormatBool(record, "IsValid");
            row.Cells["lastModified"] = GetString(record, "LastModifiedDate");
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseFlowRows(IEnumerable<JsonElement> records)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var label = GetString(record, "Label");
            var row = new OrgInfoRow { Id = id, Summary = label ?? GetString(record, "ApiName") ?? id };
            row.Cells["label"] = label;
            row.Cells["apiName"] = GetString(record, "ApiName");
            row.Cells["processType"] = GetString(record, "ProcessType");
            row.Cells["active"] = FormatBool(record, "IsActive");
            row.Cells["lastModified"] = GetString(record, "LastModifiedDate");
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseScheduledJobRows(IEnumerable<JsonElement> records)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var name = GetNestedString(record, "CronJobDetail", "Name");
            var row = new OrgInfoRow { Id = id, Summary = name ?? id };
            row.Cells["name"] = name;
            row.Cells["jobType"] = GetNestedString(record, "CronJobDetail", "JobType");
            row.Cells["nextFireTime"] = GetString(record, "NextFireTime");
            row.Cells["previousFireTime"] = GetString(record, "PreviousFireTime");
            row.Cells["state"] = GetString(record, "State");
            row.Cells["timesTriggered"] = GetNumber(record, "TimesTriggered");
            row.Cells["created"] = GetString(record, "CreatedDate");
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseConnectedAppRows(IEnumerable<JsonElement> records)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var name = GetString(record, "Name");
            var row = new OrgInfoRow { Id = id, Summary = name ?? id };
            row.Cells["name"] = name;
            row.Cells["created"] = GetString(record, "CreatedDate");
            row.Cells["lastModified"] = GetString(record, "LastModifiedDate");
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseInstalledPackageRows(IEnumerable<JsonElement> records, IReadOnlyDictionary<string, string> versions)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var name = GetNestedString(record, "SubscriberPackage", "Name");
            var versionId = GetString(record, "SubscriberPackageVersionId");
            var row = new OrgInfoRow { Id = id, Summary = name ?? id };
            row.Cells["name"] = name;
            row.Cells["namespace"] = GetNestedString(record, "SubscriberPackage", "NamespacePrefix");
            row.Cells["version"] = versionId is not null && versions.TryGetValue(versionId, out var version) ? version : versionId;
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseLoginHistoryRows(IEnumerable<JsonElement> records, IReadOnlyDictionary<string, string> userNames)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var userId = GetString(record, "UserId");
            var user = userId is not null && userNames.TryGetValue(userId, out var name) ? name : userId;
            var row = new OrgInfoRow { Id = id, Summary = user ?? id };
            row.Cells["user"] = user;
            row.Cells["loginTime"] = GetString(record, "LoginTime");
            row.Cells["loginType"] = GetString(record, "LoginType");
            row.Cells["status"] = GetString(record, "Status");
            row.Cells["sourceIp"] = GetString(record, "SourceIp");
            row.Cells["browser"] = GetString(record, "Browser");
            row.Cells["platform"] = GetString(record, "Platform");
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseAuditTrailRows(IEnumerable<JsonElement> records)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var action = GetString(record, "Action");
            var row = new OrgInfoRow { Id = id, Summary = action ?? id };
            row.Cells["datetime"] = GetString(record, "CreatedDate");
            row.Cells["user"] = GetNestedString(record, "CreatedBy", "Name");
            row.Cells["action"] = action;
            row.Cells["section"] = GetString(record, "Section");
            row.Cells["detail"] = GetString(record, "Display");
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseRecordTypeRows(IEnumerable<JsonElement> records)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var name = GetString(record, "Name");
            var row = new OrgInfoRow { Id = id, Summary = name ?? id };
            row.Cells["sobject"] = GetString(record, "SobjectType");
            row.Cells["name"] = name;
            row.Cells["developerName"] = GetString(record, "DeveloperName");
            row.Cells["description"] = GetString(record, "Description");
            row.Cells["created"] = GetString(record, "CreatedDate");
            rows.Add(row);
        }

        return rows;
    }

    public static List<OrgInfoRow> ParseCurrencyRows(IEnumerable<JsonElement> records)
    {
        var rows = new List<OrgInfoRow>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id") ?? string.Empty;
            var isoCode = GetString(record, "IsoCode");
            var row = new OrgInfoRow { Id = id, Summary = isoCode ?? id };
            row.Cells["isoCode"] = isoCode;
            row.Cells["name"] = GetString(record, "Name");
            row.Cells["active"] = FormatBool(record, "IsActive");
            row.Cells["conversionRate"] = GetNumber(record, "ConversionRate");
            row.Cells["created"] = GetString(record, "CreatedDate");
            rows.Add(row);
        }

        return rows;
    }

    private static string? SettingsLink(string? instanceUrl, string itemId)
    {
        if (string.IsNullOrWhiteSpace(instanceUrl))
        {
            return null;
        }

        return itemId switch
        {
            "companyInfo" => OrgInfoUrlBuilder.CompanyInformation(instanceUrl),
            "sessionSettings" => OrgInfoUrlBuilder.SessionSettings(instanceUrl),
            "passwordPolicies" => OrgInfoUrlBuilder.PasswordPolicies(instanceUrl),
            "loginIpRanges" => OrgInfoUrlBuilder.LoginIpRanges(instanceUrl),
            "resourceUsage" => OrgInfoUrlBuilder.ResourceUsage(instanceUrl),
            "loginHistory" => OrgInfoUrlBuilder.LoginHistory(instanceUrl),
            "auditTrail" => OrgInfoUrlBuilder.SetupAuditTrail(instanceUrl),
            _ => null,
        };
    }

    /// <summary>パッケージバージョンの表示文字列（1.2 / 1.2.3 / 1.2.3 (build 4)）。</summary>
    public static string? FormatPackageVersion(JsonElement record)
    {
        var major = GetInt(record, "MajorVersion");
        if (major is null)
        {
            return null;
        }

        var text = $"{major}.{GetInt(record, "MinorVersion") ?? 0}";
        var patch = GetInt(record, "PatchVersion") ?? 0;
        if (patch != 0)
        {
            text += "." + patch;
        }

        var build = GetInt(record, "BuildNumber") ?? 0;
        if (build != 0)
        {
            text += $" (build {build})";
        }

        return text;
    }

    /// <summary>設定値の型（真偽値はトークン、文字列・日時・数値は raw 文字列）に応じて表示値を返す。</summary>
    private static string? FormatSettingValue(JsonElement record, string field)
    {
        if (!record.TryGetProperty(field, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => OrgInfoTokens.True,
            JsonValueKind.False => OrgInfoTokens.False,
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => property.GetRawText(),
        };
    }

    private static bool IsUnsupportedObjectError(SalesforceApiException ex) =>
        string.Equals(ex.ErrorCode, "INVALID_TYPE", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("is not supported", StringComparison.OrdinalIgnoreCase);

    /// <summary>ユーザ一覧キャッシュ（Id → 名前）を読み込む（ログイン履歴のユーザー名解決用）。</summary>
    private Dictionary<string, string> LoadUserNames(string orgKey)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = _cache.GetSection(orgKey, OrgInfoSections.Users);
        if (section is null)
        {
            return names;
        }

        foreach (var row in section.Rows)
        {
            var name = row.Get("name");
            if (!string.IsNullOrEmpty(row.Id) && !string.IsNullOrEmpty(name))
            {
                names[row.Id] = name;
            }
        }

        return names;
    }

    // ---- 内部処理 ----

    private async Task<List<JsonElement>> QueryAllRecordsAsync(string targetOrg, string soql, bool useToolingApi, CancellationToken cancellationToken)
    {
        var records = new List<JsonElement>();

        using var document = await _rest.QueryAsync(targetOrg, soql, useToolingApi, cancellationToken).ConfigureAwait(false);
        AddRecords(document.RootElement, records);
        var next = GetString(document.RootElement, "nextRecordsUrl");
        var pages = 1;
        while (!string.IsNullOrEmpty(next) && records.Count < MaxRowsPerSection && pages < MaxPages)
        {
            using var page = await _rest.GetPageAsync(targetOrg, next!, cancellationToken).ConfigureAwait(false);
            AddRecords(page.RootElement, records);
            next = GetString(page.RootElement, "nextRecordsUrl");
            pages++;
        }

        if (!string.IsNullOrEmpty(next))
        {
            _log.Warn($"組織情報の取得行数が上限に達したため残りを省略しました（{records.Count} 行 / 上限 {MaxRowsPerSection}）: {soql}");
        }

        if (records.Count > MaxRowsPerSection)
        {
            records.RemoveRange(MaxRowsPerSection, records.Count - MaxRowsPerSection);
        }

        return records;
    }

    private static void AddRecords(JsonElement root, List<JsonElement> records)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("records", out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var record in array.EnumerateArray())
        {
            records.Add(record.Clone());
        }
    }

    private static JsonElement? FirstRecord(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("records", out var array)
            && array.ValueKind == JsonValueKind.Array
            && array.GetArrayLength() > 0)
        {
            return array[0].Clone();
        }

        return null;
    }

    private static string TargetOrg(OrgInfo org) =>
        string.IsNullOrWhiteSpace(org.Alias) ? org.Username : org.Alias!;

    private static string FieldsApiName(string sectionId) => sectionId[OrgInfoSections.FieldsPrefix.Length..];

    private static string? InstanceUrl(OrgInfo org) => string.IsNullOrWhiteSpace(org.InstanceUrl) ? null : org.InstanceUrl;

    private static string? UserLink(string? instanceUrl, string id) =>
        string.IsNullOrWhiteSpace(instanceUrl) || string.IsNullOrEmpty(id) ? null : OrgInfoUrlBuilder.UserDetail(instanceUrl, id);

    private static string? ProfileLink(string? instanceUrl, string id) =>
        string.IsNullOrWhiteSpace(instanceUrl) || string.IsNullOrEmpty(id) ? null : OrgInfoUrlBuilder.ProfileDetail(instanceUrl, id);

    private static string? ObjectLink(string? instanceUrl, string apiName) =>
        string.IsNullOrWhiteSpace(instanceUrl) || string.IsNullOrEmpty(apiName) ? null : OrgInfoUrlBuilder.ObjectDetail(instanceUrl, apiName);

    private static string? BuildAddress(JsonElement record)
    {
        var parts = new[]
            {
                GetString(record, "Street"),
                GetString(record, "City"),
                GetString(record, "State"),
                GetString(record, "PostalCode"),
                GetString(record, "Country"),
            }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Replace('\r', ' ').Replace('\n', ' ').Trim());

        var address = string.Join(", ", parts);
        return address.Length == 0 ? null : address;
    }

    private static string? FormatLimit(JsonElement limits, string key, string? suffix)
    {
        if (limits.ValueKind != JsonValueKind.Object
            || !limits.TryGetProperty(key, out var entry)
            || entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var max = GetNumber(entry, "Max");
        var remaining = GetNumber(entry, "Remaining");
        if (max is null && remaining is null)
        {
            return null;
        }

        var text = string.Format(CultureInfo.InvariantCulture, "{0} / {1}", remaining ?? "?", max ?? "?");
        return string.IsNullOrEmpty(suffix) ? text : $"{text} {suffix}";
    }

    private static string? FormatBool(JsonElement element, string propertyName) =>
        GetBool(element, propertyName) switch
        {
            true => OrgInfoTokens.True,
            false => OrgInfoTokens.False,
            _ => null,
        };

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetNestedString(JsonElement element, string objectProperty, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(objectProperty, out var nested)
        && nested.ValueKind == JsonValueKind.Object
            ? GetString(nested, propertyName)
            : null;

    /// <summary>文字列配列（例: FieldDefinition.ReferenceTo）をカンマ区切りにする。実レスポンスは { referenceTo: [...] } 形式。</summary>
    private static string? GetJoinedStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var array))
        {
            return null;
        }

        if (array.ValueKind == JsonValueKind.Object
            && array.TryGetProperty("referenceTo", out var inner)
            && inner.ValueKind == JsonValueKind.Array)
        {
            array = inner;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
            {
                parts.Add(text);
            }
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static int? GetInt(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    private static string? GetNumber(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt64(out var longValue)
            ? longValue.ToString(CultureInfo.InvariantCulture)
            : value.GetDouble().ToString("0.###", CultureInfo.InvariantCulture);
    }
}
