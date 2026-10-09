using Avalonia;
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

    /// <summary>組織比較: 1 レコード分の項目別詳細ウィンドウを開く（非モーダル）。</summary>
    public void OpenCompareRecordDetail(CompareRecordDetailModel detail)
    {
        var viewModel = _services.GetRequiredService<CompareRecordDetailViewModel>();
        viewModel.Initialize(detail);
        ShowOwned(new CompareRecordDetailWindow(viewModel));
    }

    /// <summary>
    /// 子ウィンドウを表示する。所有関係（Show(owner)）は付けない: 所有ウィンドウは常に親より前面に固定され、
    /// メインウィンドウを前に出せなくなるため。基準ウィンドウの中央へ自前で配置する（null・サイズ未指定は既定位置）。
    /// </summary>
    private void ShowOwned(Window window)
    {
        if (_top.Current is Window reference && !ReferenceEquals(reference, window) &&
            !double.IsNaN(window.Width) && !double.IsNaN(window.Height))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            var scale = reference.RenderScaling;
            var width = window.Width * scale;
            var height = window.Height * scale;
            var x = reference.Position.X + (int)(((reference.ClientSize.Width * scale) - width) / 2);
            var y = reference.Position.Y + (int)(((reference.ClientSize.Height * scale) - height) / 2);
            window.Position = new PixelPoint(x, y);
        }

        window.Show();
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

    public void OpenOrgManage(OrgInfo? initial)
    {
        var viewModel = _services.GetRequiredService<OrgManageViewModel>();
        viewModel.Initialize(initial);
        ShowOwned(new OrgManageWindow(viewModel));
    }

    /// <summary>ETL 移行ウィンドウを開く（非モーダル・複数同時表示可）。</summary>
    public void OpenEtl(OrgInfo? org)
    {
        var viewModel = _services.GetRequiredService<EtlViewModel>();
        viewModel.Initialize(org);
        ShowOwned(new EtlWindow(viewModel));
    }

    public void OpenBackupRecords(string backupId, BackupObjectInfo info, string displayName, OrgInfo? currentOrg)
    {
        var viewModel = _services.GetRequiredService<BackupRecordsViewModel>();
        viewModel.Initialize(backupId, info, displayName, currentOrg);
        ShowOwned(new BackupRecordsWindow(viewModel));
    }

    public void OpenBackupCompareRecords(string backupIdA, string backupIdB, string objectName, string displayName, OrgInfo? currentOrg)
    {
        var viewModel = _services.GetRequiredService<BackupCompareRecordsViewModel>();
        viewModel.Initialize(backupIdA, backupIdB, objectName, displayName, currentOrg);
        ShowOwned(new BackupCompareRecordsWindow(viewModel));
    }

    /// <summary>デバッグログ解析ウィンドウを開く（非モーダル・複数同時表示可）。</summary>
    public void OpenLogAnalyzer(DebugLogAnalysis analysis, string orgLabel, string sourceLabel)
    {
        var viewModel = _services.GetRequiredService<LogAnalyzerViewModel>();
        viewModel.Initialize(analysis, orgLabel, sourceLabel);
        ShowOwned(new LogAnalyzerWindow(viewModel));
    }

    /// <summary>項目の使用箇所（フィールド影響分析）ウィンドウを開く（非モーダル）。</summary>
    public void OpenFieldUsage(OrgInfo org, string objectApiName, string fieldApiName, string? fieldLabel = null)
    {
        var viewModel = _services.GetRequiredService<FieldUsageViewModel>();
        viewModel.Initialize(org, objectApiName, fieldApiName, fieldLabel);
        ShowOwned(new FieldUsageWindow(viewModel));
    }

    /// <summary>ようこそ画面をモーダルで開く（「設定を開く」が選ばれたら openSettings を呼ぶ）。</summary>
    public void OpenWelcome(Action? openSettings = null)
    {
        _log.Info("ようこそ画面を開きます（モーダル）");
        var viewModel = _services.GetRequiredService<WelcomeViewModel>();
        if (openSettings is not null)
        {
            viewModel.OpenSettingsRequested += openSettings;
        }

        var window = new WelcomeWindow(viewModel);
        if (_top.Current is Window owner && !ReferenceEquals(owner, window))
        {
            _ = window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }

    private void NotPortedYet(string feature)
    {
        _log.Info($"Avalonia 版: {feature} ウィンドウは未移植（Phase C–E で実装）");
        _dialogs.Info(
            $"The {feature} window is not yet available in the Avalonia build (planned for Phase C–E).",
            "SfUi");
    }
}
