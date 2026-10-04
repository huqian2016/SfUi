using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SfUi.Core;

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

    public string SubTypeText => Item.SubType;

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
    private readonly AppLog _log;
    private string? _target;
    private CancellationTokenSource? _cts;

    public MigrationInventoryViewModel(MigrationInventoryService inventory, AppLog log)
    {
        _inventory = inventory;
        _log = log;
        RowsView = new ListCollectionView(Rows);
        RowsView.Filter = o => o is MigrationItemRowViewModel row && Matches(row);
    }

    public ObservableCollection<MigrationItemRowViewModel> Rows { get; } = new();

    public ICollectionView RowsView { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasRows;

    [ObservableProperty]
    private string _searchText = string.Empty;

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

    partial void OnSearchTextChanged(string value) => RowsView.Refresh();

    /// <summary>棚卸しを再取得する。</summary>
    [RelayCommand]
    private async Task FetchAsync() => await LoadAsync();

    /// <summary>取得を中止する。</summary>
    [RelayCommand]
    private void CancelLoad() => CancelBackgroundWork();

    /// <summary>棚卸し結果を CSV へ出力する。</summary>
    [RelayCommand]
    private void ExportCsv()
    {
        var current = Current;
        if (current is null || Rows.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = UiText.T("OrgManage_InvCsvTitle"),
            FileName = $"migration-inventory-{_target?.Replace('@', '_')}-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            Filter = UiText.T("OrgManage_InvCsvFilter"),
        };

        if (dialog.ShowDialog() != true)
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

            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusMessage = UiText.T("OrgManage_InvCsvSavedFmt", dialog.FileName);
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
            RowsView.Refresh();
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

    private bool Matches(MigrationItemRowViewModel row) =>
        string.IsNullOrWhiteSpace(SearchText)
        || row.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
        || row.ApiNameText.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
        || row.ObjectText.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
        || row.KindText.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
}
