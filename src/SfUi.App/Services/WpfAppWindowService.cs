using System.Windows;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.Services;

/// <summary>WPF の WindowFactory 群へ委譲する IAppWindowService 実装。</summary>
public sealed class WpfAppWindowService : IAppWindowService
{
    private readonly OrgInfoWindowFactory _orgInfo;
    private readonly CompareOrgsWindowFactory _compareOrgs;
    private readonly CompareRecordDetailWindowFactory _compareRecordDetail;
    private readonly DataIoWindowFactory _dataIo;
    private readonly BackupWindowFactory _backup;
    private readonly OrgManageWindowFactory _orgManage;
    private readonly BackupRecordsWindowFactory _records;
    private readonly BackupCompareRecordsWindowFactory _compareRecords;
    private readonly LogAnalyzerWindowFactory _logAnalyzer;
    private readonly FieldUsageWindowFactory _fieldUsage;
    private readonly WelcomeWindowFactory _welcome;

    public WpfAppWindowService(
        OrgInfoWindowFactory orgInfo,
        CompareOrgsWindowFactory compareOrgs,
        CompareRecordDetailWindowFactory compareRecordDetail,
        DataIoWindowFactory dataIo,
        BackupWindowFactory backup,
        OrgManageWindowFactory orgManage,
        BackupRecordsWindowFactory records,
        BackupCompareRecordsWindowFactory compareRecords,
        LogAnalyzerWindowFactory logAnalyzer,
        FieldUsageWindowFactory fieldUsage,
        WelcomeWindowFactory welcome)
    {
        _orgInfo = orgInfo;
        _compareOrgs = compareOrgs;
        _compareRecordDetail = compareRecordDetail;
        _dataIo = dataIo;
        _backup = backup;
        _orgManage = orgManage;
        _records = records;
        _compareRecords = compareRecords;
        _logAnalyzer = logAnalyzer;
        _fieldUsage = fieldUsage;
        _welcome = welcome;
    }

    public void OpenOrgInfo(OrgInfo org) => _orgInfo.Open(org, ActiveOwner());

    public void OpenCompareOrgs(IReadOnlyList<OrgInfo> orgs) => _compareOrgs.Open(orgs, ActiveOwner());

    public void OpenCompareRecordDetail(CompareRecordDetailModel detail) => _compareRecordDetail.Open(detail, ActiveOwner());

    public void OpenDataIo(OrgInfo org, string? objectName = null, string? initialSoql = null) =>
        _dataIo.Open(org, objectName, initialSoql, ActiveOwner());

    public void OpenBackup(OrgInfo org) => _backup.Open(org, ActiveOwner());

    public void OpenOrgManage(OrgInfo? initial) => _orgManage.Open(initial, ActiveOwner());

    public void OpenBackupRecords(string backupId, BackupObjectInfo info, string displayName, OrgInfo? currentOrg) =>
        _records.Open(backupId, info, displayName, currentOrg, ActiveOwner());

    public void OpenBackupCompareRecords(string backupIdA, string backupIdB, string objectName, string displayName, OrgInfo? currentOrg) =>
        _compareRecords.Open(backupIdA, backupIdB, objectName, displayName, currentOrg, ActiveOwner());

    public void OpenLogAnalyzer(DebugLogAnalysis analysis, string orgLabel, string sourceLabel) =>
        _logAnalyzer.Open(analysis, orgLabel, sourceLabel, ActiveOwner());

    public void OpenFieldUsage(OrgInfo org, string objectApiName, string fieldApiName, string? fieldLabel = null) =>
        _fieldUsage.Open(org, objectApiName, fieldApiName, fieldLabel, ActiveOwner());

    public void OpenWelcome(Action? openSettings = null) => _welcome.Show(openSettings, ActiveOwner());

    /// <summary>アクティブ ウィンドウ（無ければメイン ウィンドウ）を親として返す。</summary>
    private static Window? ActiveOwner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;
}
