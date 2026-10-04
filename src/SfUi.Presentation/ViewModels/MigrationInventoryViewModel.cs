using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>移行棚卸し タブの 1 行（Workflow ルール / プロセスビルダー / フロー）。</summary>
public sealed class MigrationItemRowViewModel
{
    public MigrationItemRowViewModel(MigrationItem item)
    {
        Item = item;
    }

    public MigrationItem Item { get; }

    public string KindText => Item.Kind switch
    {
        MigrationItemKind.WorkflowRule => UiText.T("OrgManage_KindWorkflowRule"),
        MigrationItemKind.ProcessBuilder => UiText.T("OrgManage_KindProcessBuilder"),
        _ => UiText.T("OrgManage_KindFlow"),
    };

    public string Name => Item.Name;

    public string ApiNameText => Item.ApiName;

    public string ObjectText => Item.ObjectName;

    public string ActiveText => Item.Active switch
    {
        true => UiText.T("OrgManage_ActiveYes"),
        false => UiText.T("OrgManage_ActiveNo"),
        _ => "—",
    };

    public string SubTypeText => Item.SubType switch
    {
        "onCreateOrTriggeringUpdate" => UiText.T("OrgManage_TriggerOnCreateOrTriggeringUpdate"),
        "onAllChanges" => UiText.T("OrgManage_TriggerOnAllChanges"),
        "onCreateOnly" => UiText.T("OrgManage_TriggerOnCreateOnly"),
        _ => Item.SubType,
    };

    public string LastModifiedText => Item.LastModified is { } date
        ? date.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        : "—";

    /// <summary>UIA 用の表示名。</summary>
    public string Display => $"{KindText} {Name}";
}

/// <summary>移行棚卸し タブ（Workflow / プロセスビルダー / フローの一覧と CSV 出力）。</summary>
public sealed partial class MigrationInventoryViewModel : ObservableObject
{
    private readonly MigrationInventoryService _inventory;
    private readonly OrgManageService _manage;
    private readonly AppLog _log;
    private readonly IFilePickerService _filePicker;
    private readonly ObservableFilterView<MigrationItemRowViewModel> _rowsView;
    private string? _target;
    private CancellationTokenSource? _cts;

    public MigrationInventoryViewModel(MigrationInventoryService inventory, OrgManageService manage, IFilePickerService filePicker, AppLog log)
    {
        _inventory = inventory;
        _manage = manage;
        _filePicker = filePicker;
        _log = log;
        _rowsView = new ObservableFilterView<MigrationItemRowViewModel>(Rows, Matches);
        _kindOptions = new[]
        {
            UiText.T("Common_All"),
            UiText.T("OrgManage_KindWorkflowRule"),
            UiText.T("OrgManage_KindProcessBuilder"),
            UiText.T("OrgManage_KindFlow"),
        };
    }

    public ObservableCollection<MigrationItemRowViewModel> Rows { get; } = new();

    public ObservableCollection<MigrationItemRowViewModel> RowsView => _rowsView.Items;

    /// <summary>種別フィルタの選択肢（すべて / Workflow ルール / プロセスビルダー / フロー）。</summary>
    private readonly IReadOnlyList<string> _kindOptions;

    public IReadOnlyList<string> KindOptions => _kindOptions;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasRows;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _kindFilterIndex;

    [ObservableProperty]
    private bool _showActiveOnly;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _warningText = string.Empty;

    [ObservableProperty]
    private bool _hasWarning;

    /// <summary>直近の取得結果（CSV 出力に使う）。</summary>
    public MigrationInventory? Current { get; private set; }

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
        SummaryText = string.Empty;
        StatusMessage = string.Empty;
        WarningText = string.Empty;
        HasWarning = false;
        Current = null;
        if (!string.IsNullOrWhiteSpace(target))
        {
            _ = LoadAsync();
        }
    }

    partial void OnSearchTextChanged(string value) => _rowsView.Refresh();

    partial void OnKindFilterIndexChanged(int value) => _rowsView.Refresh();

    partial void OnShowActiveOnlyChanged(bool value) => _rowsView.Refresh();

    /// <summary>行の Setup ページ（Workflow ルール / フロー）をブラウザーで開く。</summary>
    [RelayCommand]
    private async Task OpenItemAsync(MigrationItemRowViewModel? row)
    {
        if (row is null)
        {
            StatusMessage = UiText.T("OrgManage_NeedRow");
            return;
        }

        var target = _target;
        if (string.IsNullOrWhiteSpace(target))
        {
            StatusMessage = UiText.T("OrgManage_NeedOrg");
            return;
        }

        var path = BuildSetupPath(row.Item);
        var result = await _manage.OpenPathAsync(target, path, CancellationToken.None);
        StatusMessage = result.Success
            ? UiText.T("OrgManage_OpenDoneFmt", path)
            : UiText.T("OrgManage_OpenFailedFmt", result.Message);
    }

    /// <summary>種別に応じた Setup ページのパスを組み立てる（フローは Id で詳細ページ）。</summary>
    public static string BuildSetupPath(MigrationItem item) => item.Kind switch
    {
        MigrationItemKind.WorkflowRule => "lightning/setup/WorkflowRules/home",
        _ when !string.IsNullOrWhiteSpace(item.Id) => $"lightning/setup/Flows/page?address=/{item.Id}",
        _ => "lightning/setup/Flows/home",
    };

    /// <summary>棚卸しを再取得する。</summary>
    [RelayCommand]
    private async Task FetchAsync() => await LoadAsync();

    /// <summary>取得を中止する。</summary>
    [RelayCommand]
    private void CancelLoad() => CancelBackgroundWork();

    /// <summary>棚卸し結果を CSV へ出力する。</summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var current = Current;
        if (current is null || Rows.Count == 0)
        {
            return;
        }

        var suggestedName = $"migration-inventory-{_target?.Replace('@', '_')}-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        var path = await _filePicker.SaveFileAsync(UiText.T("OrgManage_InvCsvTitle"), suggestedName, UiText.T("OrgManage_InvCsvFilter"));
        if (path is null)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(',',
                CsvExporter.Escape(UiText.T("OrgManage_ColKind")),
                CsvExporter.Escape(UiText.T("OrgManage_ColName")),
                CsvExporter.Escape(UiText.T("OrgManage_ColApiName")),
                CsvExporter.Escape(UiText.T("OrgManage_ColObject")),
                CsvExporter.Escape(UiText.T("OrgManage_ColActive")),
                CsvExporter.Escape(UiText.T("OrgManage_ColSubType")),
                CsvExporter.Escape(UiText.T("OrgManage_ColLastModified"))));
            foreach (var row in Rows)
            {
                builder.AppendLine(string.Join(',',
                    CsvExporter.Escape(row.KindText),
                    CsvExporter.Escape(row.Name),
                    CsvExporter.Escape(row.ApiNameText),
                    CsvExporter.Escape(row.ObjectText),
                    CsvExporter.Escape(row.ActiveText),
                    CsvExporter.Escape(row.SubTypeText),
                    CsvExporter.Escape(row.LastModifiedText)));
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusMessage = UiText.T("OrgManage_InvCsvSavedFmt", path);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("組織管理: 棚卸しの CSV 出力に失敗しました", ex);
        }
    }

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
            var inventory = await _inventory.FetchAsync(target, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            Current = inventory;
            Rows.Clear();
            foreach (var item in inventory.Items)
            {
                Rows.Add(new MigrationItemRowViewModel(item));
            }

            HasRows = Rows.Count > 0;
            _rowsView.Refresh();
            SummaryText = UiText.T(
                "OrgManage_InvSummaryFmt",
                inventory.Total,
                inventory.WorkflowCount,
                inventory.ProcessBuilderCount,
                inventory.FlowCount,
                inventory.ActiveCount);
            HasWarning = inventory.WorkflowError is { Length: > 0 };
            WarningText = HasWarning ? UiText.T("OrgManage_InvWorkflowErrorFmt", inventory.WorkflowError!) : string.Empty;
            StatusMessage = HasRows
                ? UiText.T("OrgManage_InvLoadedFmt", DateTime.Now)
                : UiText.T("OrgManage_InvEmpty");
        }
        catch (OperationCanceledException)
        {
            // 組織切り替え・キャンセル
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error($"組織管理: 移行棚卸しの取得に失敗しました（{target}）", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool Matches(MigrationItemRowViewModel row)
    {
        if (KindFilterIndex > 0 && (int)row.Item.Kind != KindFilterIndex - 1)
        {
            return false;
        }

        if (ShowActiveOnly && row.Item.Active != true)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(SearchText)
            || row.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || row.ApiNameText.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || row.ObjectText.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || row.KindText.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }
}
