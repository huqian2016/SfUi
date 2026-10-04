using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.App.Services;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>比較結果グリッドの 1 行（オブジェクト単位）。</summary>
public sealed class BackupCompareObjectRowViewModel
{
    public BackupCompareObjectRowViewModel(BackupCompareObjectResult result) => Result = result;

    public BackupCompareObjectResult Result { get; }

    public string Name => Result.Name;

    public string Label => Result.Label;

    public string CountAText => Result.CountA.ToString("N0", CultureInfo.InvariantCulture);

    public string CountBText => Result.CountB.ToString("N0", CultureInfo.InvariantCulture);

    public string AddedText => Result.Added.ToString("N0", CultureInfo.InvariantCulture);

    public string RemovedText => Result.Removed.ToString("N0", CultureInfo.InvariantCulture);

    public string ChangedText => Result.Changed.ToString("N0", CultureInfo.InvariantCulture);

    public bool HasDiff => Result.HasDiff;

    public bool OnlyInA => Result.OnlyInA;

    public bool OnlyInB => Result.OnlyInB;

    public bool HasError => Result.HasError;

    public string StatusText => Result.HasError
        ? UiText.T("BackupCompare_StatusError")
        : Result.OnlyInA
            ? UiText.T("BackupCompare_StatusOnlyA")
            : Result.OnlyInB
                ? UiText.T("BackupCompare_StatusOnlyB")
                : Result.HasDiff
                    ? UiText.T("BackupCompare_StatusDiff")
                    : UiText.T("BackupCompare_StatusSame");

    public string? ErrorText => Result.ErrorA ?? Result.ErrorB;
}

/// <summary>バックアップ比較タブ（2 つのバックアップを選んでオブジェクト単位に比較する）。</summary>
public sealed partial class CompareTabViewModel : ObservableObject
{
    private readonly BackupCompareService _compare;
    private readonly BackupCompareRecordsWindowFactory _recordsFactory;
    private readonly AppLog _log;
    private CancellationTokenSource? _runCts;

    public CompareTabViewModel(BackupCompareService compare, BackupCompareRecordsWindowFactory recordsFactory, AppLog log)
    {
        _compare = compare;
        _recordsFactory = recordsFactory;
        _log = log;
        BackupsViewA = new ListCollectionView(Backups);
        BackupsViewA.Filter = o => o is BackupListItemViewModel item && Matches(item, SearchTextA);
        BackupsViewB = new ListCollectionView(Backups);
        BackupsViewB.Filter = o => o is BackupListItemViewModel item && Matches(item, SearchTextB);
        ResultsView = new ListCollectionView(Results);
        ResultsView.Filter = o => o is BackupCompareObjectRowViewModel row && (!DiffOnly || row.HasDiff);
    }

    public ObservableCollection<BackupListItemViewModel> Backups { get; } = new();

    public ICollectionView BackupsViewA { get; }

    public ICollectionView BackupsViewB { get; }

    public ObservableCollection<BackupCompareObjectRowViewModel> Results { get; } = new();

    public ICollectionView ResultsView { get; }

    [ObservableProperty]
    private BackupListItemViewModel? _selectedBackupA;

    [ObservableProperty]
    private BackupListItemViewModel? _selectedBackupB;

    [ObservableProperty]
    private string _searchTextA = string.Empty;

    [ObservableProperty]
    private string _searchTextB = string.Empty;

    [ObservableProperty]
    private bool _diffOnly;

    [ObservableProperty]
    private bool _isComparing;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>バックアップ一覧を読み込む（選択中 ID は維持）。</summary>
    public void RefreshBackups()
    {
        var previousA = SelectedBackupA?.Id;
        var previousB = SelectedBackupB?.Id;
        Backups.Clear();
        foreach (var metadata in _compare.ListBackupMetadata())
        {
            Backups.Add(new BackupListItemViewModel(metadata));
        }

        BackupsViewA.Refresh();
        BackupsViewB.Refresh();
        SelectedBackupA = Backups.FirstOrDefault(b => string.Equals(b.Id, previousA, StringComparison.Ordinal));
        SelectedBackupB = Backups.FirstOrDefault(b => string.Equals(b.Id, previousB, StringComparison.Ordinal));

        // 初回は 2 件選んでおくとすぐ比較できる
        if (SelectedBackupA is null && Backups.Count > 0)
        {
            SelectedBackupA = Backups[0];
        }

        if (SelectedBackupB is null && Backups.Count > 1)
        {
            SelectedBackupB = Backups[1];
        }
    }

    partial void OnSearchTextAChanged(string value) => BackupsViewA.Refresh();

    partial void OnSearchTextBChanged(string value) => BackupsViewB.Refresh();

    partial void OnDiffOnlyChanged(bool value) => ResultsView.Refresh();

    private static bool Matches(BackupListItemViewModel item, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return search
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(token => item.SearchBlob.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>2 つのバックアップを比較する。</summary>
    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task CompareAsync()
    {
        if (SelectedBackupA is null || SelectedBackupB is null
            || string.Equals(SelectedBackupA.Id, SelectedBackupB.Id, StringComparison.Ordinal))
        {
            StatusMessage = UiText.T("BackupCompare_NeedTwo");
            return;
        }

        IsComparing = true;
        Results.Clear();
        HasResults = false;
        SummaryText = string.Empty;
        _runCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<BackupCompareProgress>(p =>
                StatusMessage = string.IsNullOrEmpty(p.ObjectName)
                    ? string.Empty
                    : UiText.T("BackupCompare_RunningFmt", p.Done + 1, p.Total, p.ObjectName));
            var result = await _compare.CompareAsync(SelectedBackupA.Id, SelectedBackupB.Id, progress, _runCts.Token);

            foreach (var objectResult in result.Objects)
            {
                Results.Add(new BackupCompareObjectRowViewModel(objectResult));
            }

            ResultsView.Refresh();
            HasResults = true;
            StatusMessage = string.Empty;
            SummaryText = result.DiffObjectCount > 0
                ? UiText.T("BackupCompare_SummaryFmt", result.DiffObjectCount, result.Added, result.Removed, result.Changed)
                : UiText.T("BackupCompare_NoDiff");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("バックアップ比較に失敗しました", ex);
        }
        finally
        {
            IsComparing = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelCompare))]
    private void CancelCompare() => _runCts?.Cancel();

    private bool CanCompare() => !IsComparing;

    private bool CanCancelCompare() => IsComparing;

    partial void OnIsComparingChanged(bool value)
    {
        CompareCommand.NotifyCanExecuteChanged();
        CancelCompareCommand.NotifyCanExecuteChanged();
    }

    /// <summary>レコード単位の比較結果を別ウィンドウで開く。</summary>
    [RelayCommand]
    private void OpenDetails(BackupCompareObjectRowViewModel? row)
    {
        if (row is null || SelectedBackupA is null || SelectedBackupB is null)
        {
            return;
        }

        try
        {
            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            _recordsFactory.Open(
                SelectedBackupA.Id, SelectedBackupB.Id, row.Name, row.Label,
                owner ?? Application.Current?.MainWindow);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error($"比較のレコード詳細ウィンドウを開けませんでした: {row.Name}", ex);
        }
    }

    /// <summary>ウィンドウを閉じるときに実行中の比較を止める。</summary>
    public void CancelBackgroundWork() => _runCts?.Cancel();
}
