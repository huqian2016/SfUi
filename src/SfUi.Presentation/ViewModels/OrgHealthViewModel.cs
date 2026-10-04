using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>ヘルス タブの 1 行（API 使用量）。</summary>
public sealed class OrgLimitRowViewModel
{
    public OrgLimitRowViewModel(OrgLimit limit)
    {
        Limit = limit;
    }

    public OrgLimit Limit { get; }

    public string Label => Limit.Label;

    public string Key => Limit.Key;

    public string UsedText => FormatNumber(Limit.Used);

    public string MaxText => FormatNumber(Limit.Max);

    public double BarValue => Limit.Percent ?? 0;

    public string PercentText => Limit.Percent is { } percent
        ? percent.ToString("F1", CultureInfo.InvariantCulture) + "%"
        : "—";

    /// <summary>使用率が 80% 以上（警告色）。</summary>
    public bool IsWarning => Limit.Percent >= 80;

    private static string FormatNumber(decimal? value) =>
        value is null ? "—" : value.Value.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>ヘルス タブ（REST /limits の表示）。組織が変わると自動で再取得する。</summary>
public sealed partial class OrgHealthViewModel : ObservableObject
{
    private readonly SalesforceRestClient _rest;
    private readonly AppLog _log;
    private readonly ObservableFilterView<OrgLimitRowViewModel> _rowsView;
    private string? _target;
    private CancellationTokenSource? _cts;

    public OrgHealthViewModel(SalesforceRestClient rest, AppLog log)
    {
        _rest = rest;
        _log = log;
        _rowsView = new ObservableFilterView<OrgLimitRowViewModel>(Rows, Matches);
    }

    public ObservableCollection<OrgLimitRowViewModel> Rows { get; } = new();

    public ObservableCollection<OrgLimitRowViewModel> RowsView => _rowsView.Items;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasRows;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>対象組織を切り替える（同名なら何もしない）。</summary>
    public void SetTarget(string? target)
    {
        if (string.Equals(_target, target, StringComparison.Ordinal))
        {
            return;
        }

        _target = target;
        CancelBackgroundWork();
        Rows.Clear();
        HasRows = false;
        StatusMessage = string.Empty;
        if (!string.IsNullOrWhiteSpace(target))
        {
            _ = LoadAsync();
        }
    }

    partial void OnFilterTextChanged(string value) => _rowsView.Refresh();

    /// <summary>使用量を再取得する。</summary>
    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    /// <summary>取得を中止する。</summary>
    [RelayCommand]
    private void CancelLoad() => CancelBackgroundWork();

    public void CancelBackgroundWork() => _cts?.Cancel();

    private async Task LoadAsync()
    {
        var target = _target;
        if (string.IsNullOrWhiteSpace(target) || IsLoading)
        {
            return;
        }

        IsLoading = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            using var document = await _rest.GetLimitsAsync(target, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            var limits = OrgLimitsParser.Parse(document.RootElement);
            Rows.Clear();
            foreach (var limit in limits
                .OrderByDescending(l => l.Percent ?? -1)
                .ThenBy(l => l.Key, StringComparer.OrdinalIgnoreCase))
            {
                Rows.Add(new OrgLimitRowViewModel(limit));
            }

            HasRows = Rows.Count > 0;
            _rowsView.Refresh();
            StatusMessage = HasRows
                ? UiText.T("OrgManage_HealthFetchedFmt", Rows.Count, DateTime.Now)
                : UiText.T("OrgManage_HealthEmpty");
        }
        catch (OperationCanceledException)
        {
            // 組織切り替え・キャンセル
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error($"組織管理: 使用量の取得に失敗しました（{target}）", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool Matches(OrgLimitRowViewModel row) =>
        string.IsNullOrWhiteSpace(FilterText)
        || row.Label.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
        || row.Key.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
}
