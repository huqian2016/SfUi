using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>ダッシュボードの 1 カード（グラフ付きレポート 1 件）。</summary>
public sealed partial class DashboardCard : ObservableObject
{
    public DashboardCard(ReportDefinition definition)
    {
        Definition = definition;
        _title = definition.DisplayName;
    }

    public ReportDefinition Definition { get; }

    public string ReportId => Definition.Id;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private byte[]? _chartPng;

    [ObservableProperty]
    private string _rowsText = "";

    [ObservableProperty]
    private string _errorText = "";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>グラフ画像があるか。</summary>
    public bool HasChart => ChartPng is { Length: > 0 };

    /// <summary>取得に失敗したか。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    partial void OnChartPngChanged(byte[]? value) => OnPropertyChanged(nameof(HasChart));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));
}

/// <summary>
/// レポートのダッシュボード。グラフ付きの保存レポートをまとめて実行し、
/// グラフを並べて表示する（手動更新 / N 秒ごとの自動更新）。
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly ReportDefinitionStore _store;
    private readonly ILocalReportRunner _runner;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _loopCts;
    private bool _disposed;

    public DashboardViewModel(
        ReportDefinitionStore store,
        ILocalReportRunner runner,
        AppPaths paths,
        AppLog log,
        TimeProvider? time = null)
    {
        _store = store;
        _runner = runner;
        _paths = paths;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    public ObservableCollection<DashboardCard> Cards { get; } = new();

    public ObservableCollection<OrgInfo> Orgs { get; } = new();

    [ObservableProperty]
    private OrgInfo? _selectedOrg;

    [ObservableProperty]
    private string _intervalText = "60";

    [ObservableProperty]
    private bool _isAutoRefresh;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _nextRefreshText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasCards;

    // ── 表示用ラベル ──
    public string Title => UiText.T("Dashboard_Title");

    public string LabelRefresh => UiText.T("Dashboard_Refresh");

    public string LabelAuto => UiText.T("Dashboard_Auto");

    public string LabelOrg => UiText.T("Dashboard_Org");

    public string LabelInterval => UiText.T("Dashboard_Interval");

    public string LabelNoCharts => UiText.T("Dashboard_NoCharts");

    /// <summary>対象組織とレポート一覧を読み込む。</summary>
    public void Initialize(IReadOnlyList<OrgInfo> orgs, OrgInfo? currentOrg, bool refresh = true)
    {
        Orgs.Clear();
        foreach (var org in orgs)
        {
            Orgs.Add(org);
        }

        SelectedOrg = currentOrg is not null
            ? Orgs.FirstOrDefault(o => string.Equals(o.Username, currentOrg.Username, StringComparison.OrdinalIgnoreCase)) ?? currentOrg
            : Orgs.FirstOrDefault();

        Reload();

        if (refresh && Cards.Count > 0)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnSelectedOrgChanged(OrgInfo? value)
    {
        if (!_disposed && Cards.Count > 0)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnIsAutoRefreshChanged(bool value)
    {
        if (value)
        {
            StartLoop();
        }
        else
        {
            StopLoop();
        }
    }

    /// <summary>保存レポートのうちグラフ設定があるものをカードにする。</summary>
    public void Reload()
    {
        var charted = _store.Reports.Where(r => r.Chart is { IsEnabled: true }).ToList();
        Cards.Clear();
        foreach (var report in charted)
        {
            Cards.Add(new DashboardCard(report));
        }

        HasCards = Cards.Count > 0;
        StatusText = HasCards ? "" : LabelNoCharts;
    }

    /// <summary>全カードを再実行してグラフを描き直す。</summary>
    [RelayCommand]
    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>全カードを再実行してグラフを描き直す。</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || Cards.Count == 0)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var updated = 0;
            foreach (var card in Cards)
            {
                cancellationToken.ThrowIfCancellationRequested();
                card.IsBusy = true;
                try
                {
                    var result = await _runner
                        .RunAsync(card.Definition, SelectedOrg?.Username, cancellationToken)
                        .ConfigureAwait(true);

                    var data = ReportChartData.Build(result.Table, card.Definition.Chart, result.ReportName);
                    card.ChartPng = data is null ? null : ReportChartRenderer.RenderPng(data, 520, 260);
                    card.RowsText = UiText.T("Dashboard_RowsFmt", result.Table.RowCount.ToString("N0", CultureInfo.CurrentCulture));
                    card.ErrorText = data is null ? UiText.T("Dashboard_NoChartData") : "";
                    updated++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    card.ChartPng = null;
                    card.RowsText = "";
                    card.ErrorText = UiText.T("Report_FailedFmt", ex.Message);
                    _log.Warn($"ダッシュボード: {card.Title} の取得に失敗しました: {ex.Message}");
                }
                finally
                {
                    card.IsBusy = false;
                }
            }

            StatusText = UiText.T("Dashboard_DoneFmt", updated, _time.GetLocalNow().ToString("HH:mm:ss", CultureInfo.CurrentCulture));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private int NormalizeInterval()
    {
        if (!int.TryParse(IntervalText?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var seconds))
        {
            seconds = 60;
        }

        return Math.Clamp(seconds, 10, 3600);
    }

    private void StartLoop()
    {
        StopLoop();
        var cts = new CancellationTokenSource();
        _loopCts = cts;
        _ = LoopAsync(cts.Token);
    }

    private void StopLoop()
    {
        var cts = _loopCts;
        _loopCts = null;
        NextRefreshText = "";
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
            cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // 既に破棄済み
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var seconds = NormalizeInterval();
                NextRefreshText = UiText.T(
                    "Dashboard_NextFmt",
                    _time.GetLocalNow().AddSeconds(seconds).ToString("HH:mm:ss", CultureInfo.CurrentCulture));
                await Task.Delay(TimeSpan.FromSeconds(seconds), _time, cancellationToken).ConfigureAwait(true);

                if (IsBusy)
                {
                    continue;
                }

                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // 自動更新の停止
        }
        finally
        {
            NextRefreshText = "";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopLoop();
    }
}
