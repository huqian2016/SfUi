using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>バックアップ タブの 1 オブジェクト行。</summary>
public sealed partial class BackupObjectRowViewModel : ObservableObject
{
    private readonly Action<BackupObjectRowViewModel>? _selectionChanged;

    public BackupObjectRowViewModel(DataIoObject obj, Action<BackupObjectRowViewModel>? selectionChanged = null)
    {
        Name = obj.Name;
        Label = string.IsNullOrWhiteSpace(obj.Label) ? obj.Name : obj.Label;
        _selectionChanged = selectionChanged;
    }

    public string Name { get; }

    public string Label { get; }

    /// <summary>一覧の表示名（UIA 名にも使う）。</summary>
    public string Display => string.Equals(Name, Label, StringComparison.OrdinalIgnoreCase) ? Name : $"{Label} ({Name})";

    public bool ShowName => !string.Equals(Name, Label, StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int? _count;

    public string CountText => Count is null ? "–" : Count.Value.ToString("N0", CultureInfo.InvariantCulture);

    partial void OnIsSelectedChanged(bool value) => _selectionChanged?.Invoke(this);

    partial void OnCountChanged(int? value) => OnPropertyChanged(nameof(CountText));
}

/// <summary>バックアップ タブ（オブジェクト選択・件数・ラベル/説明・実行）。</summary>
public sealed partial class BackupTabViewModel : ObservableObject
{
    private readonly BackupService _backups;
    private readonly SObjectDescribeService _describes;
    private readonly BackupStateStore _state;
    private readonly AppLog _log;
    private OrgInfo? _org;
    private bool _loaded;
    private bool _restoringSelection;
    private CancellationTokenSource? _countsCts;
    private CancellationTokenSource? _runCts;

    public BackupTabViewModel(BackupService backups, SObjectDescribeService describes, BackupStateStore state, AppLog log)
    {
        _backups = backups;
        _describes = describes;
        _state = state;
        _log = log;
        ObjectsView = new ListCollectionView(Objects);
        ObjectsView.Filter = o => o is BackupObjectRowViewModel row && Matches(row);
    }

    public ObservableCollection<BackupObjectRowViewModel> Objects { get; } = new();

    public ICollectionView ObjectsView { get; }

    public event Action? BackupCompleted;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isFetchingCounts;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _countsProgress = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    private string TargetOrg => _org is null ? string.Empty : (string.IsNullOrWhiteSpace(_org.Alias) ? _org.Username : _org.Alias!);

    public void Initialize(OrgInfo org) => _org = org;

    /// <summary>オブジェクト一覧を読み込み、記憶した選択と件数キャッシュを適用する（1 回）。</summary>
    public async Task LoadAsync()
    {
        if (_loaded || _org is null)
        {
            return;
        }

        _loaded = true;
        var org = _org;
        try
        {
            var objects = await _describes.ListObjectsAsync(TargetOrg);
            var saved = new HashSet<string>(_state.GetSelectedObjects(org.Username), StringComparer.OrdinalIgnoreCase);
            var cached = _backups.GetCachedCounts(org.Username);

            _restoringSelection = true;
            Objects.Clear();
            foreach (var obj in objects.Where(o => o.Queryable).OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase))
            {
                var row = new BackupObjectRowViewModel(obj, OnRowSelectionChanged) { IsSelected = saved.Contains(obj.Name) };
                if (cached.TryGetValue(obj.Name, out var count))
                {
                    row.Count = count;
                }

                Objects.Add(row);
            }

            _restoringSelection = false;
            UpdateSummary();
            ObjectsView.Refresh();

            if (cached.Count == 0 && Objects.Count > 0)
            {
                // 初回はキャッシュがないためバックグラウンドで取得する
                _ = FetchCountsCoreAsync(clearFirst: false, auto: true);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("バックアップ: オブジェクト一覧の取得に失敗しました", ex);
        }
    }

    partial void OnSearchTextChanged(string value) => ObjectsView.Refresh();

    private bool Matches(BackupObjectRowViewModel row) =>
        string.IsNullOrWhiteSpace(SearchText)
        || row.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
        || row.Label.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

    private void OnRowSelectionChanged(BackupObjectRowViewModel row)
    {
        if (_restoringSelection || _org is null)
        {
            return;
        }

        _state.SetSelectedObjects(_org.Username, Objects.Where(o => o.IsSelected).Select(o => o.Name));
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var selected = Objects.Where(o => o.IsSelected).ToList();
        var records = selected.Sum(o => o.Count ?? 0);
        SummaryText = UiText.T("Backup_SelectedSummaryFmt", selected.Count, records);
    }

    /// <summary>件数を再取得する（キャッシュを破棄）。</summary>
    [RelayCommand]
    private async Task RefreshCountsAsync() => await FetchCountsCoreAsync(clearFirst: true, auto: false);

    private async Task FetchCountsCoreAsync(bool clearFirst, bool auto)
    {
        if (_org is null || IsFetchingCounts)
        {
            return;
        }

        var org = _org;
        if (clearFirst)
        {
            _backups.ClearCounts(org.Username);
        }

        IsFetchingCounts = true;
        _countsCts = new CancellationTokenSource();
        CountsProgress = UiText.T("Backup_CountFetchingFmt", 0, Objects.Count);
        try
        {
            var names = Objects.Select(o => o.Name).ToList();
            var progress = new Action<int, int>((done, total) =>
                OnUi(() => CountsProgress = UiText.T("Backup_CountFetchingFmt", done, total)));
            await _backups.FetchCountsAsync(TargetOrg, org.Username, names, progress, _countsCts.Token);

            var cached = _backups.GetCachedCounts(org.Username);
            foreach (var row in Objects)
            {
                if (cached.TryGetValue(row.Name, out var count))
                {
                    row.Count = count;
                }
            }

            StatusMessage = UiText.T("Backup_CountsUpdatedFmt", cached.Count);
            CountsProgress = string.Empty;
        }
        catch (OperationCanceledException)
        {
            CountsProgress = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Backup_CountFetchFailed");
            CountsProgress = string.Empty;
            _log.Error("バックアップ: 件数の取得に失敗しました", ex);
        }
        finally
        {
            IsFetchingCounts = false;
            _countsCts?.Dispose();
            _countsCts = null;
            UpdateSummary();
        }
    }

    /// <summary>選択オブジェクトをバックアップする。</summary>
    [RelayCommand(CanExecute = nameof(CanRunBackup))]
    private async Task RunBackupAsync()
    {
        if (_org is null || IsRunning)
        {
            return;
        }

        var selected = Objects.Where(o => o.IsSelected).Select(o => o.Name).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = UiText.T("Backup_NeedObjects");
            return;
        }

        var org = _org;
        IsRunning = true;
        _runCts = new CancellationTokenSource();
        try
        {
            var label = string.IsNullOrWhiteSpace(Label)
                ? UiText.T("Backup_DefaultLabelFmt", DateTime.Now)
                : Label.Trim();
            var description = string.IsNullOrWhiteSpace(Description)
                ? (string.IsNullOrWhiteSpace(org.OrgId) ? org.DisplayName : $"{org.DisplayName} / {org.OrgId}")
                : Description.Trim();
            var appVersion = typeof(BackupTabViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var progress = new Progress<BackupProgress>(p =>
                StatusMessage = UiText.T("Backup_ProgressFmt", p.Done, p.Total, p.ObjectName, PhaseText(p.Phase), p.Rows));

            var metadata = await _backups.RunBackupAsync(
                TargetOrg, org.DisplayName, org.Username, org.OrgId, appVersion,
                label, description, selected, progress, _runCts.Token);

            StatusMessage = UiText.T("Backup_CompletedFmt", metadata.Id, metadata.TotalRecords, metadata.Objects.Count);
            BackupCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Backup_FailedFmt", ex.Message);
            _log.Error("バックアップに失敗しました", ex);
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelBackup))]
    private void CancelBackup() => _runCts?.Cancel();

    private bool CanRunBackup() => !IsRunning;

    private bool CanCancelBackup() => IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        RunBackupCommand.NotifyCanExecuteChanged();
        CancelBackupCommand.NotifyCanExecuteChanged();
    }

    private static string PhaseText(string phase) => phase switch
    {
        "describe" => UiText.T("Backup_PhaseDescribe"),
        "rest" => UiText.T("Backup_PhaseRest"),
        "bulk" => UiText.T("Backup_PhaseBulk"),
        _ => "✓",
    };

    /// <summary>ウィンドウを閉じるときに実行中の処理を止める。</summary>
    public void CancelBackgroundWork()
    {
        _countsCts?.Cancel();
        _runCts?.Cancel();
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
