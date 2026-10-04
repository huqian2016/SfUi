using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>組織管理タブの 1 行（タグ・メモ・疎通結果は後から更新される）。</summary>
public sealed partial class OrgManageOrgRowViewModel : ObservableObject
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

    /// <summary>ローカルのタグ（org-manage.json）。</summary>
    [ObservableProperty]
    private string _tag = string.Empty;

    /// <summary>ローカルのメモ（org-manage.json）。</summary>
    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>疎通テストの短い表示（OK 123 ms / NG / 空）。</summary>
    [ObservableProperty]
    private string _connectionText = string.Empty;

    /// <summary>疎通テストの詳細（ツールチップ）。</summary>
    [ObservableProperty]
    private string _connectionDetail = string.Empty;

    /// <summary>最終バックアップ日時（ローカル・無ければ —）。</summary>
    [ObservableProperty]
    private string _lastBackupText = "—";
}

/// <summary>
/// 組織管理ウィンドウの ViewModel（組織一覧の管理 + ヘルス + 移行棚卸し）。
/// </summary>
public sealed partial class OrgManageViewModel : ObservableObject, IDisposable
{
    private readonly OrgService _orgs;
    private readonly OrgManageService _manage;
    private readonly OrgManageStateStore _state;
    private readonly BackupService _backups;
    private readonly AppLog _log;
    private string? _initialUsername;
    private bool _initialized;
    private bool _suspendSelection;
    private bool _disposed;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _testCts;

    public OrgManageViewModel(
        OrgService orgs,
        OrgManageService manage,
        OrgManageStateStore state,
        BackupService backups,
        OrgHealthViewModel health,
        MigrationInventoryViewModel inventory,
        AppLog log)
    {
        _orgs = orgs;
        _manage = manage;
        _state = state;
        _backups = backups;
        _log = log;
        Health = health;
        Inventory = inventory;
        _title = UiText.T("OrgManage_Title");
        OrgsView = new ListCollectionView(Orgs);
        OrgsView.Filter = o => o is OrgManageOrgRowViewModel row && Matches(row);
        UiText.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>組織一覧（既定組織が先頭）。</summary>
    public ObservableCollection<OrgManageOrgRowViewModel> Orgs { get; } = new();

    /// <summary>絞り込み適用後の組織一覧。</summary>
    public ICollectionView OrgsView { get; }

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
    private bool _isTestingConnections;

    [ObservableProperty]
    private string _testProgress = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _tagInput = string.Empty;

    [ObservableProperty]
    private string _noteInput = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>組織コマンド（再読み込み・ログイン等）が可能か。</summary>
    public bool CanInteract => !IsBusy;

    /// <summary>疎通テストを開始できるか。</summary>
    public bool CanTestConnections => !IsTestingConnections;

    /// <summary>取り消せる処理が実行中か（組織コマンド / 疎通テスト）。</summary>
    public bool CanCancel => IsBusy || IsTestingConnections;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanCancel));
    }

    partial void OnIsTestingConnectionsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTestConnections));
        OnPropertyChanged(nameof(CanCancel));
    }

    partial void OnSearchTextChanged(string value) => OrgsView.Refresh();

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

        TagInput = value?.Tag ?? string.Empty;
        NoteInput = value?.Note ?? string.Empty;

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

    /// <summary>選択中の組織のタグ・メモを保存する（ローカルのみ）。</summary>
    [RelayCommand]
    private void SaveNote()
    {
        var row = SelectedOrgRow;
        if (row is null)
        {
            StatusMessage = UiText.T("OrgManage_NeedOrg");
            return;
        }

        _state.Set(row.Username, TagInput, NoteInput);
        row.Tag = TagInput.Trim();
        row.Note = NoteInput.Trim();
        OrgsView.Refresh();
        StatusMessage = UiText.T("OrgManage_NoteSavedFmt", row.Display);
        _log.Info($"組織管理: タグ・メモを保存しました ({row.Username})");
    }

    /// <summary>選択中の組織の疎通テスト（REST で Organization を 1 件読む）。</summary>
    [RelayCommand]
    private async Task TestSelectedConnectionAsync()
    {
        var row = SelectedOrgRow;
        if (row is null)
        {
            StatusMessage = UiText.T("OrgManage_NeedOrg");
            return;
        }

        if (IsTestingConnections)
        {
            return;
        }

        IsTestingConnections = true;
        _testCts = new CancellationTokenSource();
        try
        {
            await TestRowAsync(row, _testCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = UiText.T("Common_Canceled");
        }
        finally
        {
            IsTestingConnections = false;
            TestProgress = string.Empty;
        }
    }

    /// <summary>表示中の全組織の疎通テスト（順番に実行・進捗表示・キャンセル可）。</summary>
    [RelayCommand]
    private async Task TestAllConnectionsAsync()
    {
        if (IsTestingConnections || Orgs.Count == 0)
        {
            return;
        }

        IsTestingConnections = true;
        _testCts = new CancellationTokenSource();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var total = Orgs.Count;
        var ok = 0;
        var failed = 0;
        try
        {
            var rows = Orgs.ToList();
            for (var index = 0; index < rows.Count; index++)
            {
                _testCts.Token.ThrowIfCancellationRequested();
                TestProgress = UiText.T("OrgManage_TestingFmt", index + 1, total);
                if (await TestRowAsync(rows[index], _testCts.Token))
                {
                    ok++;
                }
                else
                {
                    failed++;
                }
            }

            StatusMessage = UiText.T(
                "OrgManage_TestSummaryFmt",
                ok,
                failed,
                TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).ToString(@"m\:ss"));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = UiText.T("Common_Canceled");
        }
        finally
        {
            IsTestingConnections = false;
            TestProgress = string.Empty;
        }
    }

    private async Task<bool> TestRowAsync(OrgManageOrgRowViewModel row, CancellationToken cancellationToken)
    {
        row.ConnectionText = UiText.T("OrgManage_TestingShort");
        var result = await _manage.TestConnectionAsync(row.Username, cancellationToken);
        if (result.Success)
        {
            row.ConnectionText = UiText.T("OrgManage_TestOkShortFmt", result.Duration.TotalMilliseconds);
            row.ConnectionDetail = result.Detail;
            StatusMessage = UiText.T(
                "OrgManage_TestOkFmt",
                row.Display,
                result.Detail,
                result.Duration.TotalMilliseconds);
            return true;
        }

        row.ConnectionText = UiText.T("OrgManage_TestNgShort");
        row.ConnectionDetail = result.Message;
        StatusMessage = UiText.T("OrgManage_TestFailedFmt", row.Display, result.Message);
        return false;
    }

    /// <summary>実行中のコマンド（ログイン等）と疎通テストを取り消す。</summary>
    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        _testCts?.Cancel();
    }

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
        _testCts?.Cancel();
        _testCts?.Dispose();
        _testCts = null;
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
            RebuildRows(orgs, keep);
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

    /// <summary>
    /// 組織一覧を再構築する（選択の復元・タグ / メモ・最終バックアップの適用。選択変更イベントは抑止する）。
    /// </summary>
    private void RebuildRows(IReadOnlyList<OrgInfo> orgs, string? keepUsername)
    {
        var previous = SelectedOrgRow?.Org.Username;
        var entries = _state.GetAll();
        var lastBackups = LoadLastBackups();
        _suspendSelection = true;
        try
        {
            Orgs.Clear();
            foreach (var org in orgs)
            {
                var row = new OrgManageOrgRowViewModel(org);
                if (entries.TryGetValue(org.Username, out var entry))
                {
                    row.Tag = entry.Tag ?? string.Empty;
                    row.Note = entry.Note ?? string.Empty;
                }

                row.LastBackupText = lastBackups.TryGetValue(org.Username, out var createdAt)
                    ? createdAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    : "—";
                Orgs.Add(row);
            }

            SelectedOrgRow = Orgs.FirstOrDefault(o => string.Equals(o.Username, keepUsername, StringComparison.OrdinalIgnoreCase))
                ?? (previous is null ? null : Orgs.FirstOrDefault(o => string.Equals(o.Username, previous, StringComparison.OrdinalIgnoreCase)))
                ?? Orgs.FirstOrDefault(o => o.Org.IsDefault)
                ?? Orgs.FirstOrDefault();
        }
        finally
        {
            _suspendSelection = false;
        }

        OrgsView.Refresh();
    }

    /// <summary>組織（ユーザー名）ごとの最終バックアップ日時をローカルのバックアップから集計する。</summary>
    private Dictionary<string, DateTimeOffset> LoadLastBackups()
    {
        var map = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var backup in _backups.ListBackups())
            {
                if (string.IsNullOrWhiteSpace(backup.OrgUsername))
                {
                    continue;
                }

                if (!map.TryGetValue(backup.OrgUsername, out var current) || backup.CreatedAt > current)
                {
                    map[backup.OrgUsername] = backup.CreatedAt;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"組織管理: 最終バックアップの集計に失敗しました: {ex.Message}");
        }

        return map;
    }

    private bool Matches(OrgManageOrgRowViewModel row)
    {
        var text = SearchText.Trim();
        if (text.Length == 0)
        {
            return true;
        }

        return MatchesText(row.Username, text)
            || MatchesText(row.AliasText, text)
            || MatchesText(row.OrgIdText, text)
            || MatchesText(row.Tag, text)
            || MatchesText(row.Note, text);
    }

    private static bool MatchesText(string? value, string text) =>
        value is { Length: > 0 } && value.Contains(text, StringComparison.OrdinalIgnoreCase);

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
                    RebuildRows(orgs, keepUsername);
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
