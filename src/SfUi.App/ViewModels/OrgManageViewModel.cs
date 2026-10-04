using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>組織管理タブの 1 行。</summary>
public sealed class OrgManageOrgRowViewModel
{
    public OrgManageOrgRowViewModel(OrgInfo org)
    {
        Org = org;
    }

    public OrgInfo Org { get; }

    public string DefaultMark => Org.IsDefault ? "★" : string.Empty;

    public string AliasText => Org.Alias ?? string.Empty;

    public string Username => Org.Username;

    public string OrgIdText => Org.OrgId ?? string.Empty;

    public string TypeText => UiText.T(Org.IsSandbox ? "Backup_OrgTypeSandbox" : "Backup_OrgTypeProduction");

    public string StatusText => Org.ConnectedStatus ?? string.Empty;

    public string InstanceUrlText => Org.InstanceUrl ?? string.Empty;

    /// <summary>UIA 用の表示名。</summary>
    public string Display => Org.DisplayName;
}

/// <summary>
/// 組織管理ウィンドウの ViewModel（組織一覧の管理 + ヘルス + 移行棚卸し）。
/// </summary>
public sealed partial class OrgManageViewModel : ObservableObject, IDisposable
{
    private readonly OrgService _orgs;
    private readonly OrgManageService _manage;
    private readonly AppLog _log;
    private string? _initialUsername;
    private bool _initialized;
    private bool _suspendSelection;
    private bool _disposed;
    private CancellationTokenSource? _cts;

    public OrgManageViewModel(
        OrgService orgs,
        OrgManageService manage,
        OrgHealthViewModel health,
        MigrationInventoryViewModel inventory,
        AppLog log)
    {
        _orgs = orgs;
        _manage = manage;
        _log = log;
        Health = health;
        Inventory = inventory;
        _title = UiText.T("OrgManage_Title");
        UiText.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>組織一覧（既定組織が先頭）。</summary>
    public ObservableCollection<OrgManageOrgRowViewModel> Orgs { get; } = new();

    /// <summary>ヘルス タブの ViewModel。</summary>
    public OrgHealthViewModel Health { get; }

    /// <summary>移行棚卸し タブの ViewModel。</summary>
    public MigrationInventoryViewModel Inventory { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private OrgManageOrgRowViewModel? _selectedOrgRow;

    [ObservableProperty]
    private string _aliasInput = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>操作（再読み込み・コマンド実行）が可能か。</summary>
    public bool CanInteract => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanInteract));

    /// <summary>選択中の組織（未選択なら null）。</summary>
    public OrgInfo? SelectedOrg => SelectedOrgRow?.Org;

    /// <summary>選択中の組織の target-org 文字列（エイリアス優先）。</summary>
    public string? SelectedTarget => SelectedOrg is null
        ? null
        : (string.IsNullOrWhiteSpace(SelectedOrg.Alias) ? SelectedOrg.Username : SelectedOrg.Alias);

    partial void OnSelectedOrgRowChanged(OrgManageOrgRowViewModel? value)
    {
        if (value is not null && !string.IsNullOrWhiteSpace(value.AliasText))
        {
            AliasInput = value.AliasText;
        }

        if (!_suspendSelection)
        {
            ApplyTarget();
        }
    }

    /// <summary>ウィンドウ生成時にメインウィンドウの選択組織を受け取る（1 回）。</summary>
    public void Initialize(OrgInfo? initial)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _initialUsername = initial?.Username;
        ApplyTarget();
    }

    /// <summary>ウィンドウ表示後の初期ロード（組織一覧の取得）。</summary>
    public async Task LoadAsync() => await RefreshOrgsCoreAsync();

    /// <summary>組織一覧を再読み込みする。</summary>
    [RelayCommand]
    private async Task RefreshOrgsAsync() => await RefreshOrgsCoreAsync();

    /// <summary>選択中の組織を既定（target-org）に設定する。</summary>
    [RelayCommand]
    private async Task SetDefaultAsync()
    {
        var org = SelectedOrg;
        if (org is null)
        {
            StatusMessage = UiText.T("OrgManage_NeedOrg");
            return;
        }

        var target = SelectedTarget!;
        await RunOrgCommandAsync(
            () => _manage.SetDefaultAsync(target, CurrentToken()),
            UiText.T("OrgManage_DefaultSetFmt", org.DisplayName),
            reloadOrgs: true,
            keepUsername: org.Username);
    }

    /// <summary>選択中の組織のユーザー名へエイリアスを設定する。</summary>
    [RelayCommand]
    private async Task SetAliasAsync()
    {
        var org = SelectedOrg;
        if (org is null)
        {
            StatusMessage = UiText.T("OrgManage_NeedOrg");
            return;
        }

        var alias = AliasInput.Trim();
        if (string.IsNullOrWhiteSpace(alias))
        {
            StatusMessage = UiText.T("OrgManage_NeedAlias");
            return;
        }

        await RunOrgCommandAsync(
            () => _manage.SetAliasAsync(org.Username, alias, CurrentToken()),
            UiText.T("OrgManage_AliasSetFmt", alias, org.Username),
            reloadOrgs: true,
            keepUsername: org.Username);
    }

    /// <summary>選択中の組織をブラウザーで開く。</summary>
    [RelayCommand]
    private async Task OpenOrgAsync()
    {
        var org = SelectedOrg;
        if (org is null)
        {
            StatusMessage = UiText.T("OrgManage_NeedOrg");
            return;
        }

        await RunOrgCommandAsync(
            () => _manage.OpenOrgAsync(SelectedTarget!, CurrentToken()),
            UiText.T("OrgManage_OpenedOrgFmt", org.DisplayName),
            reloadOrgs: false,
            keepUsername: null);
    }

    /// <summary>ブラウザー ログイン（sf org login web）を実行する。</summary>
    [RelayCommand]
    private async Task LoginWebAsync()
    {
        if (!Confirm(UiText.T("OrgManage_LoginConfirm"), UiText.T("OrgManage_Title")))
        {
            return;
        }

        await RunOrgCommandAsync(
            () => _manage.LoginWebAsync(null, CurrentToken()),
            UiText.T("OrgManage_LoginDoneFmt"),
            reloadOrgs: true,
            keepUsername: null);
    }

    /// <summary>選択中の組織からログアウトする。</summary>
    [RelayCommand]
    private async Task LogoutAsync()
    {
        var org = SelectedOrg;
        if (org is null)
        {
            StatusMessage = UiText.T("OrgManage_NeedOrg");
            return;
        }

        if (!Confirm(UiText.T("OrgManage_LogoutConfirmFmt", org.DisplayName), UiText.T("OrgManage_Logout")))
        {
            return;
        }

        await RunOrgCommandAsync(
            () => _manage.LogoutAsync(SelectedTarget!, CurrentToken()),
            UiText.T("OrgManage_LoggedOutFmt", org.DisplayName),
            reloadOrgs: true,
            keepUsername: null);
    }

    /// <summary>実行中のコマンドを取り消す（ログイン等）。</summary>
    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UiText.LanguageChanged -= OnLanguageChanged;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        Health.CancelBackgroundWork();
        Inventory.CancelBackgroundWork();
    }

    private void OnLanguageChanged() => Title = UiText.T("OrgManage_Title");

    private CancellationToken CurrentToken()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    private void ApplyTarget()
    {
        var target = SelectedTarget;
        Health.SetTarget(target);
        Inventory.SetTarget(target);
    }

    private async Task RefreshOrgsCoreAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        var keep = SelectedOrgRow?.Org.Username ?? _initialUsername;
        try
        {
            var orgs = await _orgs.ListOrgsAsync(CurrentToken());
            _suspendSelection = true;
            try
            {
                Orgs.Clear();
                foreach (var org in orgs)
                {
                    Orgs.Add(new OrgManageOrgRowViewModel(org));
                }

                SelectedOrgRow = Orgs.FirstOrDefault(o => string.Equals(o.Username, keep, StringComparison.OrdinalIgnoreCase))
                    ?? Orgs.FirstOrDefault(o => o.Org.IsDefault)
                    ?? Orgs.FirstOrDefault();
            }
            finally
            {
                _suspendSelection = false;
            }

            ApplyTarget();
            StatusMessage = UiText.T("OrgManage_OrgsLoadedFmt", Orgs.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("組織管理: 組織一覧の取得に失敗しました", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>sf コマンドを実行し、結果をステータスへ反映する（必要なら組織一覧を再読み込み）。</summary>
    private async Task RunOrgCommandAsync(
        Func<Task<OrgCommandResult>> action, string successMessage, bool reloadOrgs, string? keepUsername)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await action();
            if (result.Success)
            {
                StatusMessage = successMessage;
                if (reloadOrgs)
                {
                    var orgs = await _orgs.ListOrgsAsync(CurrentToken());
                    _suspendSelection = true;
                    try
                    {
                        Orgs.Clear();
                        foreach (var org in orgs)
                        {
                            Orgs.Add(new OrgManageOrgRowViewModel(org));
                        }

                        SelectedOrgRow = Orgs.FirstOrDefault(o => string.Equals(o.Username, keepUsername, StringComparison.OrdinalIgnoreCase))
                            ?? Orgs.FirstOrDefault(o => string.Equals(o.Username, SelectedOrgRow?.Org.Username, StringComparison.OrdinalIgnoreCase))
                            ?? Orgs.FirstOrDefault();
                    }
                    finally
                    {
                        _suspendSelection = false;
                    }

                    ApplyTarget();
                }
            }
            else
            {
                StatusMessage = UiText.T("Common_FailedFmt", result.Message);
                _log.Warn($"組織管理: コマンドに失敗しました: {result.CommandLine}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("組織管理: コマンド実行で例外が発生しました", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool Confirm(string message, string caption)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        var result = owner is null
            ? MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(owner, message, caption, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }
}
