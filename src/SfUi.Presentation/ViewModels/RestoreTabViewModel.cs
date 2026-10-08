using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>復元タブのバックアップ一覧 1 件。</summary>
public sealed class BackupListItemViewModel
{
    public BackupListItemViewModel(BackupMetadata metadata) => Metadata = metadata;

    public BackupMetadata Metadata { get; }

    public string Id => Metadata.Id;

    public string Title => string.IsNullOrWhiteSpace(Metadata.Label) ? Metadata.Id : Metadata.Label;

    public string Subtitle =>
        $"{Metadata.CreatedAt:yyyy-MM-dd HH:mm} · {(string.IsNullOrWhiteSpace(Metadata.OrgDisplay) ? Metadata.OrgUsername : Metadata.OrgDisplay)}";

    public string Summary => UiText.T("Restore_ObjectSummaryFmt", Metadata.Objects.Count, Metadata.TotalRecords);

    public string Description => Metadata.Description;

    public string SearchBlob =>
        $"{Metadata.Label}\n{Metadata.Description}\n{Metadata.OrgDisplay}\n{Metadata.OrgUsername}\n{Metadata.Id}\n{Metadata.CreatedAt:yyyy-MM-dd HH:mm}".ToLowerInvariant();
}

/// <summary>復元タブの 1 オブジェクト行（件数・キー項目・詳細ボタン）。</summary>
public sealed partial class RestoreObjectRowViewModel : ObservableObject
{
    private readonly Action<RestoreObjectRowViewModel>? _selectionChanged;

    public RestoreObjectRowViewModel(BackupObjectInfo info, Action<RestoreObjectRowViewModel>? selectionChanged = null)
    {
        Info = info;
        _selectionChanged = selectionChanged;
    }

    public BackupObjectInfo Info { get; }

    public string Name => Info.Name;

    public string Label => string.IsNullOrWhiteSpace(Info.Label) ? Info.Name : Info.Label;

    public string CountText => Info.Count.ToString("N0", CultureInfo.InvariantCulture);

    public string EngineText => Info.Engine == BackupEngine.Bulk
        ? UiText.T("Backup_EngineBulk")
        : UiText.T("Backup_EngineRest");

    public string? Error => Info.Error;

    public ObservableCollection<string> KeyFields { get; } = new();

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string? _keyField = "Name";

    partial void OnIsSelectedChanged(bool value) => _selectionChanged?.Invoke(this);

    /// <summary>キー項目の候補を設定する（初回は Name を既定にする）。</summary>
    public void SetKeyOptions(IReadOnlyList<string> candidates)
    {
        KeyFields.Clear();
        foreach (var candidate in candidates)
        {
            KeyFields.Add(candidate);
        }

        if (KeyField is null || !candidates.Contains(KeyField, StringComparer.OrdinalIgnoreCase))
        {
            KeyField = candidates.Contains("Name", StringComparer.OrdinalIgnoreCase) ? "Name" : candidates.FirstOrDefault();
        }
    }
}

/// <summary>復元タブ（バックアップ選択 → オブジェクト選択 → 復元実行）。</summary>
public sealed partial class RestoreTabViewModel : ObservableObject
{
    private readonly BackupService _backups;
    private readonly SObjectDescribeService _describes;
    private readonly IAppWindowService _windows;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly AppLog _log;
    private readonly ObservableFilterView<BackupListItemViewModel> _backupsView;
    private OrgInfo? _org;
    private CancellationTokenSource? _describeCts;
    private CancellationTokenSource? _restoreCts;

    public RestoreTabViewModel(
        BackupService backups,
        SObjectDescribeService describes,
        IAppWindowService windows,
        IDialogService dialogs,
        IUiDispatcher ui,
        AppLog log)
    {
        _backups = backups;
        _describes = describes;
        _windows = windows;
        _dialogs = dialogs;
        _ui = ui;
        _log = log;
        MatchModeOptions = new[]
        {
            UiText.T("Restore_MatchAuto"),
            UiText.T("Restore_MatchId"),
            UiText.T("Restore_MatchKey"),
        };
        ExistingOptions = new[]
        {
            UiText.T("Restore_ExistingSkip"),
            UiText.T("Restore_ExistingOverwrite"),
        };
        _backupsView = new ObservableFilterView<BackupListItemViewModel>(Backups, Matches);
    }

    public ObservableCollection<BackupListItemViewModel> Backups { get; } = new();

    public ObservableCollection<BackupListItemViewModel> BackupsView => _backupsView.Items;

    public ObservableCollection<RestoreObjectRowViewModel> Objects { get; } = new();

    public ObservableCollection<RestoreObjectResult> Results { get; } = new();

    public IReadOnlyList<string> MatchModeOptions { get; }

    public IReadOnlyList<string> ExistingOptions { get; }

    [ObservableProperty]
    private BackupListItemViewModel? _selectedBackup;

    private readonly List<BackupListItemViewModel> _selectedBackups = new();

    /// <summary>複数選択されたバックアップ（View の SelectionChanged から同期される）。</summary>
    public IReadOnlyList<BackupListItemViewModel> SelectedBackups => _selectedBackups;

    /// <summary>View から呼ばれる: ListBox の複数選択を反映する。</summary>
    public void SetSelectedBackups(IReadOnlyList<BackupListItemViewModel> rows)
    {
        _selectedBackups.Clear();
        _selectedBackups.AddRange(rows);
    }

    /// <summary>一括操作の対象（複数選択が空なら現在行 1 件にフォールバック）。</summary>
    private IReadOnlyList<BackupListItemViewModel> EffectiveSelectedBackups()
        => _selectedBackups.Count > 0
            ? _selectedBackups.ToList()
            : SelectedBackup is { } row
                ? new[] { row }
                : Array.Empty<BackupListItemViewModel>();

    [ObservableProperty]
    private string _backupSearchText = string.Empty;

    [ObservableProperty]
    private int _matchModeIndex;

    [ObservableProperty]
    private int _existingIndex;

    [ObservableProperty]
    private string _orgHint = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _hasResults;

    private string TargetOrg => _org is null ? string.Empty : (string.IsNullOrWhiteSpace(_org.Alias) ? _org.Username : _org.Alias!);

    public void Initialize(OrgInfo org) => _org = org;

    /// <summary>バックアップ一覧を読み込む（選択中 ID は維持）。</summary>
    public void RefreshBackups()
    {
        var previousId = SelectedBackup?.Id;
        _selectedBackups.Clear();
        Backups.Clear();
        foreach (var metadata in _backups.ListBackups())
        {
            Backups.Add(new BackupListItemViewModel(metadata));
        }

        _backupsView.Refresh();
        SelectedBackup = Backups.FirstOrDefault(b => string.Equals(b.Id, previousId, StringComparison.Ordinal));
        if (Backups.Count == 0)
        {
            StatusMessage = UiText.T("Restore_NoBackups");
        }
    }

    partial void OnBackupSearchTextChanged(string value) => _backupsView.Refresh();

    private bool Matches(BackupListItemViewModel item) =>
        string.IsNullOrWhiteSpace(BackupSearchText)
        || BackupSearchText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(token => item.SearchBlob.Contains(token, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void ReloadBackups() => RefreshBackups();

    [RelayCommand]
    private void DeleteBackup()
    {
        var rows = EffectiveSelectedBackups();
        if (rows.Count == 0)
        {
            StatusMessage = UiText.T("Common_NeedRow");
            return;
        }

        var caption = UiText.T("Main_Backup");
        if (rows.Count == 1)
        {
            var label = rows[0].Title;
            if (!_dialogs.Confirm(UiText.T("Restore_DeleteConfirmFmt", label), caption))
            {
                return;
            }

            try
            {
                _backups.DeleteBackup(rows[0].Id);
                StatusMessage = UiText.T("Restore_DeletedFmt", label);
                RefreshBackups();
            }
            catch (Exception ex)
            {
                StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
                _log.Error($"バックアップの削除に失敗しました: {rows[0].Id}", ex);
            }

            return;
        }

        if (!_dialogs.Confirm(
                UiText.T("Restore_DeleteConfirmMultiFmt", rows.Count, Environment.NewLine),
                caption))
        {
            return;
        }

        var deleted = 0;
        var failed = 0;
        foreach (var row in rows)
        {
            try
            {
                _backups.DeleteBackup(row.Id);
                deleted++;
            }
            catch (Exception ex)
            {
                failed++;
                _log.Error($"バックアップの削除に失敗しました: {row.Id}", ex);
            }
        }

        RefreshBackups();
        StatusMessage = UiText.T("Restore_DeletedMultiFmt", deleted, failed);
    }

    partial void OnSelectedBackupChanged(BackupListItemViewModel? value) => _ = LoadBackupObjectsAsync();

    /// <summary>選択バックアップのオブジェクト一覧を読み込む。</summary>
    private async Task LoadBackupObjectsAsync()
    {
        var item = SelectedBackup;
        _describeCts?.Cancel();
        Objects.Clear();
        Results.Clear();
        HasResults = false;
        UpdateSummary();
        UpdateOrgHint();

        if (item is null)
        {
            return;
        }

        StatusMessage = string.Empty;
        var rows = item.Metadata.Objects
            .Select(info => new RestoreObjectRowViewModel(info, _ => UpdateSummary()))
            .ToList();
        foreach (var row in rows)
        {
            Objects.Add(row);
        }

        UpdateSummary();
        await PopulateKeyFieldsAsync(rows);
    }

    /// <summary>オブジェクトごとのキー項目候補（Name・外部 ID・文字列項目）を describe から作る。</summary>
    private async Task PopulateKeyFieldsAsync(IReadOnlyList<RestoreObjectRowViewModel> rows)
    {
        if (_org is null)
        {
            return;
        }

        _describeCts = new CancellationTokenSource();
        var ct = _describeCts.Token;
        using var semaphore = new SemaphoreSlim(4);
        var tasks = rows.Select(async row =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var describe = await _describes.DescribeAsync(TargetOrg, row.Name, cancellationToken: ct).ConfigureAwait(false);
                var candidates = BuildKeyCandidates(describe);
                OnUi(() => row.SetKeyOptions(candidates));
            }
            catch (OperationCanceledException)
            {
                // ウィンドウを閉じる / 選択変更で中断
            }
            catch (Exception ex)
            {
                _log.Warn($"復元: {row.Name} のキー項目候補を取得できません ({ex.Message})");
                OnUi(() => row.SetKeyOptions(new[] { "Name" }));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static List<string> BuildKeyCandidates(DataIoObjectDescribe describe)
    {
        var list = new List<string>();
        var names = describe.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Contains("Name"))
        {
            list.Add("Name");
        }

        foreach (var field in describe.Fields.Where(f => f.ExternalId).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(field.Name);
        }

        if (list.Count == 0)
        {
            list.AddRange(describe.Fields
                .Where(f => string.Equals(f.Type, "string", StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .Select(f => f.Name));
        }

        if (list.Count == 0)
        {
            list.Add("Name");
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void UpdateSummary()
    {
        var selected = Objects.Where(o => o.IsSelected).ToList();
        var records = selected.Sum(o => o.Info.Count);
        SummaryText = UiText.T("Backup_SelectedSummaryFmt", selected.Count, records);
    }

    private void UpdateOrgHint()
    {
        if (SelectedBackup is null || _org is null)
        {
            OrgHint = string.Empty;
            return;
        }

        var same = !string.IsNullOrEmpty(_org.OrgId)
            && !string.IsNullOrEmpty(SelectedBackup.Metadata.OrgId)
            && string.Equals(_org.OrgId, SelectedBackup.Metadata.OrgId, StringComparison.OrdinalIgnoreCase);
        OrgHint = UiText.T(same ? "Restore_SameOrgHint" : "Restore_DifferentOrgHint");
    }

    /// <summary>レコード詳細ウィンドウを開く。</summary>
    [RelayCommand]
    private void OpenRecords(RestoreObjectRowViewModel? row)
    {
        if (row is null || SelectedBackup is null)
        {
            return;
        }

        try
        {
            _windows.OpenBackupRecords(SelectedBackup.Id, row.Info, row.Label, _org);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error($"レコード詳細ウィンドウを開けませんでした: {row.Name}", ex);
        }
    }

    /// <summary>選択オブジェクトをバックアップから復元する。</summary>
    [RelayCommand(CanExecute = nameof(CanRunRestore))]
    private async Task RunRestoreAsync()
    {
        if (_org is null || SelectedBackup is null || IsRunning)
        {
            return;
        }

        var selected = Objects.Where(o => o.IsSelected).Select(o => o.Info).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = UiText.T("Restore_NeedObjects");
            return;
        }

        var mode = MatchModeIndex switch
        {
            1 => RestoreMatchMode.Id,
            2 => RestoreMatchMode.Key,
            _ => RestoreMatchMode.Auto,
        };
        var existing = ExistingIndex == 1 ? RestoreExistingAction.Overwrite : RestoreExistingAction.Skip;
        var keyFields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Objects.Where(o => o.IsSelected))
        {
            keyFields[row.Name] = string.IsNullOrWhiteSpace(row.KeyField) ? "Name" : row.KeyField;
        }

        IsRunning = true;
        Results.Clear();
        HasResults = false;
        _restoreCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<RestoreProgress>(p =>
                StatusMessage = UiText.T("Restore_ProgressFmt", p.Done, p.Total, p.ObjectName, PhaseText(p.Phase), p.Processed, p.Records));
            var summary = await _backups.RunRestoreAsync(
                TargetOrg,
                _org.OrgId,
                SelectedBackup.Metadata,
                selected,
                new RestoreOptions(mode, existing, keyFields),
                progress,
                _restoreCts.Token);

            foreach (var result in summary.Objects)
            {
                Results.Add(result);
            }

            HasResults = Results.Count > 0;
            StatusMessage = UiText.T(
                "Restore_CompletedFmt", summary.Created, summary.Updated, summary.Undeleted, summary.Skipped, summary.Failed);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Restore_FailedFmt", ex.Message);
            _log.Error("復元に失敗しました", ex);
        }
        finally
        {
            IsRunning = false;
            _restoreCts?.Dispose();
            _restoreCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelRestore))]
    private void CancelRestore() => _restoreCts?.Cancel();

    private bool CanRunRestore() => !IsRunning;

    private bool CanCancelRestore() => IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        RunRestoreCommand.NotifyCanExecuteChanged();
        CancelRestoreCommand.NotifyCanExecuteChanged();
    }

    private static string PhaseText(string phase) => phase switch
    {
        "match" => UiText.T("Restore_PhaseMatch"),
        "restore" => UiText.T("Restore_PhaseRestore"),
        _ => "✓",
    };

    /// <summary>ウィンドウを閉じるときに実行中の処理を止める。</summary>
    public void CancelBackgroundWork()
    {
        _describeCts?.Cancel();
        _restoreCts?.Cancel();
    }

    private void OnUi(Action action)
    {
        if (_ui.CheckAccess())
        {
            action();
        }
        else
        {
            _ui.Post(action);
        }
    }
}
