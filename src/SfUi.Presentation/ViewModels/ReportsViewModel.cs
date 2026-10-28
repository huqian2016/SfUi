using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>グループ集計 1 行の表示用。</summary>
public sealed class ReportGroupRow
{
    public required string Label { get; init; }

    public required string CountText { get; init; }

    public required string MetricsText { get; init; }

    public override string ToString() => $"{Label} ({CountText})";
}

/// <summary>結果グリッド 1 行（列インデクサーで Avalonia の動的列から参照する）。</summary>
public sealed class ReportGridRow
{
    public ReportGridRow(IReadOnlyList<string?> cells) => Cells = cells;

    public IReadOnlyList<string?> Cells { get; }

    public string this[int index] => index >= 0 && index < Cells.Count ? Cells[index] ?? string.Empty : string.Empty;
}

/// <summary>
/// ローカル レポート ウィンドウの ViewModel。
/// 保存済みレポートの一覧 / 実行 / リアルタイム更新 / 出力を担当する。
/// </summary>
public sealed partial class ReportsViewModel : ObservableObject, IDisposable
{
    private readonly ReportDefinitionStore _store;
    private readonly LocalReportRunner _runner;
    private readonly IReportEditorDialog _editor;
    private readonly IReportExportDialog _reportDialog;
    private readonly IDialogService _dialogs;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly TimeProvider _time;

    private IReadOnlyList<OrgInfo> _orgs = Array.Empty<OrgInfo>();
    private CancellationTokenSource? _liveCts;
    private LocalReportResult? _lastResult;
    private bool _suppressSelection;

    public ReportsViewModel(
        ReportDefinitionStore store,
        LocalReportRunner runner,
        IReportEditorDialog editor,
        IReportExportDialog reportDialog,
        IDialogService dialogs,
        AppPaths paths,
        AppLog log,
        TimeProvider? time = null)
    {
        _store = store;
        _runner = runner;
        _editor = editor;
        _reportDialog = reportDialog;
        _dialogs = dialogs;
        _paths = paths;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    // ── 一覧 ──
    public ObservableCollection<ReportDefinition> Reports { get; } = new();

    [ObservableProperty]
    private ReportDefinition? _selectedReport;

    // ── 結果 ──
    [ObservableProperty]
    private DataView? _resultView;

    /// <summary>結果グリッド用の行（Avalonia は動的列をコードビハインドで作るためこちらを使う）。</summary>
    public ObservableCollection<ReportGridRow> GridRows { get; } = new();

    /// <summary>結果グリッドの列名。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _gridColumns = Array.Empty<string>();

    /// <summary>グループ集計（グループ化未指定なら空）。</summary>
    public ObservableCollection<ReportGroupRow> Groups { get; } = new();

    [ObservableProperty]
    private bool _hasGroups;

    [ObservableProperty]
    private string _groupsHeading = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _lastRunText = "";

    [ObservableProperty]
    private string _nextUpdateText = "";

    [ObservableProperty]
    private string _rowCountText = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private string _truncatedWarning = "";

    [ObservableProperty]
    private bool _hasTruncatedWarning;

    [ObservableProperty]
    private string _liveIntervalText = "";

    /// <summary>直近の実行結果（出力・テスト用）。</summary>
    public LocalReportResult? LastResult => _lastResult;

    /// <summary>直近の差分（リアルタイム更新のハイライト用）。</summary>
    public ReportDiff LastDiff { get; private set; } = ReportDiff.Empty;

    // ── 表示ラベル ──
    public string Title => UiText.T("Reports_Title");

    public string LabelNew => UiText.T("Reports_New");

    public string LabelEdit => UiText.T("Reports_Edit");

    public string LabelDuplicate => UiText.T("Reports_Duplicate");

    public string LabelDelete => UiText.T("Reports_Delete");

    public string LabelRun => UiText.T("Reports_Run");

    public string LabelLive => UiText.T("Reports_Live");

    public string LabelInterval => UiText.T("Reports_Interval");

    public string LabelLastRun => UiText.T("Reports_LastRun");

    public string LabelNextRun => UiText.T("Reports_NextRun");

    public string LabelRuns => UiText.T("Reports_Runs");

    public string LabelExport => UiText.T("Report_Export");

    public string LabelNoReports => UiText.T("Reports_NoReports");

    public string LabelNoSelection => UiText.T("Reports_NoSelection");

    public string LabelGroups => UiText.T("Reports_GroupsHeading");

    public string SelectedOrgName => SelectedOrg?.DisplayName ?? "";

    /// <summary>現在の組織。</summary>
    public OrgInfo? SelectedOrg { get; private set; }

    /// <summary>初期化（組織一覧と現在の組織を受け取り、保存済みレポートを読み込む）。</summary>
    public void Initialize(IReadOnlyList<OrgInfo> orgs, OrgInfo? currentOrg)
    {
        _orgs = orgs;
        SelectedOrg = currentOrg ?? orgs.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedOrgName));
        GroupsHeading = UiText.T("Reports_GroupsHeading");
        ReloadReports(null);
    }

    /// <summary>一覧を読み込み直す（選択 Id を維持）。</summary>
    public void ReloadReports(string? selectId)
    {
        var keep = selectId ?? SelectedReport?.Id;
        _suppressSelection = true;
        Reports.Clear();
        foreach (var report in _store.Reports)
        {
            Reports.Add(report);
        }

        _suppressSelection = false;
        SelectedReport = Reports.FirstOrDefault(r => r.Id == keep) ?? Reports.FirstOrDefault();
    }

    partial void OnSelectedReportChanged(ReportDefinition? value)
    {
        if (_suppressSelection)
        {
            return;
        }

        StopLive();
        _lastResult = null;
        ResultView = null;
        GridRows.Clear();
        GridColumns = Array.Empty<string>();
        Groups.Clear();
        HasGroups = false;
        TruncatedWarning = string.Empty;
        HasTruncatedWarning = false;
        RowCountText = string.Empty;
        LastRunText = string.Empty;
        NextUpdateText = string.Empty;

        if (value is null)
        {
            StatusText = UiText.T("Reports_NoSelection");
            return;
        }

        LiveIntervalText = (ReportLivePolicy.Normalize(value.RefreshSeconds) == 0
            ? ReportLivePolicy.DefaultRefreshSeconds
            : ReportLivePolicy.Normalize(value.RefreshSeconds)).ToString(CultureInfo.CurrentCulture);
        StatusText = UiText.T("Common_Ready");

        if (value.RefreshSeconds > 0)
        {
            StartLive();
        }
        else
        {
            _ = RunAsync();
        }
    }

    /// <summary>1 回実行する。</summary>
    [RelayCommand]
    private async Task RunAsync()
    {
        try
        {
            await RunInternalAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (ReportValidationException ex)
        {
            StatusText = UiText.T("Common_FailedFmt", string.Join(" / ", ex.Errors));
            _log.Warn($"レポートの検証に失敗しました: {SelectedReport?.DisplayName}");
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("レポートの実行に失敗しました", ex);
        }
        finally
        {
            IsRunning = false;
            UpdateNextUpdateText();
        }
    }

    private async Task<LocalReportResult?> RunInternalAsync(CancellationToken cancellationToken)
    {
        if (SelectedReport is null || IsRunning)
        {
            return null;
        }

        var definition = SelectedReport;
        IsRunning = true;
        StatusText = UiText.T("Common_Running");
        var result = await _runner.RunAsync(definition, SelectedOrg?.Username, cancellationToken).ConfigureAwait(true);
        ApplyResult(definition, result);
        StatusText = UiText.T("Reports_StatusDoneFmt", result.RowCount.ToString("N0", CultureInfo.CurrentCulture), result.Duration.TotalSeconds, result.Engine);
        IsRunning = false;
        return result;
    }

    /// <summary>リアルタイム更新の開始 / 停止（チェックボックスの双方向バインド用）。</summary>
    [RelayCommand]
    private void ToggleLive() => IsLive = !IsLive;

    partial void OnIsLiveChanged(bool value)
    {
        if (value)
        {
            StartLiveCore();
        }
        else
        {
            StopLiveCore();
        }
    }

    /// <summary>リアルタイム更新を開始する。</summary>
    public void StartLive() => IsLive = true;

    /// <summary>リアルタイム更新を停止する。</summary>
    public void StopLive() => IsLive = false;

    private void StartLiveCore()
    {
        if (SelectedReport is null || _liveCts is not null)
        {
            return;
        }

        var seconds = ReportLivePolicy.Normalize(SelectedReport.RefreshSeconds);
        if (seconds == 0)
        {
            seconds = ReportLivePolicy.DefaultRefreshSeconds;
        }

        _liveCts = new CancellationTokenSource();
        _ = LiveLoopAsync(seconds, _liveCts.Token);
        UpdateNextUpdateText();
    }

    private void StopLiveCore()
    {
        var cts = _liveCts;
        _liveCts = null;
        NextUpdateText = string.Empty;
        try
        {
            cts?.Cancel();
            cts?.Dispose();
        }
        catch
        {
            // 停止時の例外は無視する
        }
    }

    private async Task LiveLoopAsync(int seconds, CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = ReportLivePolicy.NextDelay(seconds, failures);
            UpdateNextUpdateText(delay);
            try
            {
                await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await RunInternalAsync(cancellationToken).ConfigureAwait(true);
                failures = 0;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                failures++;
                _log.Warn($"リアルタイム更新に失敗しました ({failures}): {ex.Message}");
                if (ReportLivePolicy.ShouldStop(failures))
                {
                    StatusText = UiText.T("Reports_AutoStopped");
                    StopLive();
                    return;
                }
            }
        }
    }

    private void ApplyResult(ReportDefinition definition, LocalReportResult result)
    {
        LastDiff = ReportDiff.Compute(_lastResult, result);
        _lastResult = result;

        var table = new DataTable();
        foreach (var column in result.Table.Columns)
        {
            table.Columns.Add(column, typeof(string));
        }

        foreach (var row in result.Table.Rows)
        {
            var values = new object[result.Table.Columns.Count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = i < row.Count ? row[i] ?? string.Empty : string.Empty;
            }

            table.Rows.Add(values);
        }

        ResultView = table.DefaultView;

        GridColumns = result.Table.Columns.ToList();
        GridRows.Clear();
        foreach (var row in result.Table.Rows)
        {
            GridRows.Add(new ReportGridRow(row));
        }

        Groups.Clear();
        foreach (var group in result.Groups)
        {
            Groups.Add(new ReportGroupRow
            {
                Label = group.Label,
                CountText = group.RowCount.ToString("N0", CultureInfo.CurrentCulture),
                MetricsText = string.Join(" / ", group.Metrics.Select(m => $"{m.Label}: {m.Text}")),
            });
        }

        if (result.Totals is not null && result.Groups.Count > 0)
        {
            Groups.Add(new ReportGroupRow
            {
                Label = result.Totals.Label,
                CountText = result.Totals.RowCount.ToString("N0", CultureInfo.CurrentCulture),
                MetricsText = string.Join(" / ", result.Totals.Metrics.Select(m => $"{m.Label}: {m.Text}")),
            });
        }

        HasGroups = Groups.Count > 0;
        RowCountText = result.RowCount.ToString("N0", CultureInfo.CurrentCulture);
        LastRunText = result.RanAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        HasTruncatedWarning = result.Truncated;
        TruncatedWarning = result.Truncated
            ? UiText.T("Reports_TruncatedWarning", definition.RowLimit.ToString("N0", CultureInfo.CurrentCulture))
            : string.Empty;

        if (LastDiff.HasChanges)
        {
            StatusText = UiText.T("Reports_DiffFmt", LastDiff.AddedRows.Count, LastDiff.ChangedRows.Count, LastDiff.RemovedRows);
        }
    }

    private void UpdateNextUpdateText()
    {
        var seconds = ReportLivePolicy.Normalize(SelectedReport?.RefreshSeconds ?? 0);
        if (seconds == 0)
        {
            seconds = ReportLivePolicy.DefaultRefreshSeconds;
        }

        UpdateNextUpdateText(TimeSpan.FromSeconds(seconds));
    }

    private void UpdateNextUpdateText(TimeSpan delay)
    {
        NextUpdateText = IsLive
            ? UiText.T("Reports_NextRun") + " " + _time.GetLocalNow().Add(delay).ToString("HH:mm:ss", CultureInfo.CurrentCulture)
            : string.Empty;
    }

    [RelayCommand]
    private async Task NewReportAsync()
    {
        var created = await _editor.EditAsync(null, SelectedOrg?.Username).ConfigureAwait(true);
        if (created is null)
        {
            return;
        }

        _store.Upsert(created);
        ReloadReports(created.Id);
        StatusText = UiText.T("Common_SavedFmt", created.DisplayName);
    }

    [RelayCommand]
    private async Task EditReportAsync()
    {
        if (SelectedReport is null)
        {
            return;
        }

        var edited = await _editor.EditAsync(SelectedReport, SelectedOrg?.Username).ConfigureAwait(true);
        if (edited is null)
        {
            return;
        }

        _store.Upsert(edited);
        ReloadReports(edited.Id);
        await RunAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void DuplicateReport()
    {
        if (SelectedReport is null)
        {
            return;
        }

        var original = SelectedReport;
        var copy = new ReportDefinition
        {
            Id = ReportDefinition.NewId(),
            Name = $"{original.DisplayName} (copy)",
            Description = original.Description,
            ObjectName = original.ObjectName,
            TargetOrg = original.TargetOrg,
            Columns = original.Columns.Select(c => new ReportColumn { Field = c.Field, Label = c.Label, Hidden = c.Hidden, Format = c.Format, Width = c.Width }).ToList(),
            Filters = original.Filters.Select(f => new ReportFilter { Field = f.Field, Operator = f.Operator, Value = f.Value, IncludeNulls = f.IncludeNulls }).ToList(),
            FilterLogic = original.FilterLogic,
            Sorts = original.Sorts.Select(s => new ReportSort { Field = s.Field, Direction = s.Direction }).ToList(),
            Groupings = original.Groupings.Select(g => new ReportGrouping { Field = g.Field, Direction = g.Direction, DateGranularity = g.DateGranularity }).ToList(),
            Aggregates = original.Aggregates.Select(a => new ReportAggregate { Field = a.Field, Function = a.Function, Label = a.Label }).ToList(),
            RowLimit = original.RowLimit,
            UseBulk = original.UseBulk,
            RefreshSeconds = original.RefreshSeconds,
            DisplayMode = original.DisplayMode,
        };

        _store.Upsert(copy);
        ReloadReports(copy.Id);
        StatusText = UiText.T("Common_SavedFmt", copy.DisplayName);
    }

    [RelayCommand]
    private void DeleteReport()
    {
        if (SelectedReport is null)
        {
            return;
        }

        var target = SelectedReport;
        if (!_dialogs.ConfirmDestructive(UiText.T("Reports_ConfirmDeleteFmt", target.DisplayName), UiText.T("Reports_Delete")))
        {
            return;
        }

        StopLive();
        _store.Delete(target.Id);
        ReloadReports(null);
    }

    /// <summary>レポート結果をファイルへ出力する。</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_lastResult is null)
        {
            StatusText = UiText.T("Reports_NoSelection");
            return;
        }

        var document = _lastResult.ToDocument();
        var baseName = SafeFileName(_lastResult.ReportName) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var outcome = await ReportExportRunner
            .RunAsync(_reportDialog, document, baseName, _paths.ExportsDirectory)
            .ConfigureAwait(true);

        StatusText = outcome.Canceled
            ? UiText.T("Common_Canceled")
            : outcome.Error is { } error
                ? UiText.T("Report_FailedFmt", error)
                : UiText.T("Report_DoneFmt", outcome.Result?.Files.Count ?? 0, _paths.ExportsDirectory);
    }

    /// <summary>仕上げ（ウィンドウを閉じるとき）。</summary>
    public void Dispose()
    {
        StopLive();
    }

    private static string SafeFileName(string name)
    {
        var invalid = new string(Path.GetInvalidFileNameChars());
        var cleaned = Regex.Replace(name, "[" + Regex.Escape(invalid) + "]", "_").Trim();
        return cleaned.Length == 0 ? "report" : cleaned;
    }
}
