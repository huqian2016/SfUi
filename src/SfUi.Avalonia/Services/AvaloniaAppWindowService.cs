using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.ViewModels;
using SfUi.Avalonia.Views;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.Avalonia.Services;

/// <summary>
/// Avalonia 版の IAppWindowService。未移植のウィンドウは案内を表示する（Phase D–E で順次移植）。
/// </summary>
public sealed class AvaloniaAppWindowService : IAppWindowService
{
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;
    private readonly IServiceProvider _services;
    private readonly TopLevelAccessor _top;

    public AvaloniaAppWindowService(IDialogService dialogs, AppLog log, IServiceProvider services, TopLevelAccessor top)
    {
        _dialogs = dialogs;
        _log = log;
        _services = services;
        _top = top;
    }

    /// <summary>組織情報ウィンドウを開く（非モーダル・複数同時表示可）。</summary>
    public void OpenOrgInfo(OrgInfo org)
    {
        var viewModel = _services.GetRequiredService<OrgInfoViewModel>();
        viewModel.Initialize(org);
        ShowOwned(new OrgInfoWindow(viewModel));
    }

    /// <summary>組織比較ウィンドウを開く（非モーダル・複数同時表示可）。</summary>
    public void OpenCompareOrgs(IReadOnlyList<OrgInfo> orgs)
    {
        var viewModel = _services.GetRequiredService<CompareOrgsViewModel>();
        viewModel.Initialize(orgs);
        ShowOwned(new CompareOrgsWindow(viewModel));
    }

    private void ShowOwned(Window window)
    {
        if (_top.Current is Window owner && !ReferenceEquals(owner, window))
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    public void OpenDataIo(OrgInfo org, string? objectName = null, string? initialSoql = null)
    {
        var viewModel = _services.GetRequiredService<DataIoViewModel>();
        viewModel.Initialize(org, objectName, initialSoql);
        ShowOwned(new DataIoWindow(viewModel));
    }

    public void OpenBackup(OrgInfo org)
    {
        var viewModel = _services.GetRequiredService<BackupViewModel>();
        viewModel.Initialize(org);
        ShowOwned(new BackupWindow(viewModel));
    }

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
