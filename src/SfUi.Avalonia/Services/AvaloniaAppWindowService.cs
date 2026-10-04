using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.Avalonia.Services;

/// <summary>
/// Avalonia 版の IAppWindowService。各ウィンドウは Phase C–E で移植するため、
/// 現時点では未移植である旨を表示する。
/// </summary>
public sealed class AvaloniaAppWindowService : IAppWindowService
{
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;

    public AvaloniaAppWindowService(IDialogService dialogs, AppLog log)
    {
        _dialogs = dialogs;
        _log = log;
    }

    public void OpenOrgInfo(OrgInfo org) => NotPortedYet("Org Info");

    public void OpenCompareOrgs(IReadOnlyList<OrgInfo> orgs) => NotPortedYet("Compare Orgs");

    public void OpenDataIo(OrgInfo org, string? objectName = null, string? initialSoql = null) => NotPortedYet("Data I/O");

    public void OpenBackup(OrgInfo org) => NotPortedYet("Backup & Restore");

    public void OpenOrgManage(OrgInfo? initial) => NotPortedYet("Org Management");

    public void OpenBackupRecords(string backupId, BackupObjectInfo info, string displayName, OrgInfo? currentOrg) =>
        NotPortedYet("Backup Records");

    public void OpenBackupCompareRecords(string backupIdA, string backupIdB, string objectName, string displayName, OrgInfo? currentOrg) =>
        NotPortedYet("Backup Compare Records");

    private void NotPortedYet(string feature)
    {
        _log.Info($"Avalonia 版: {feature} ウィンドウは未移植（Phase C–E で実装）");
        _dialogs.Info(
            $"The {feature} window is not yet available in the Avalonia build (planned for Phase C–E).",
            "SfUi");
    }
}
