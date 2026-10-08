using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>項目の使用箇所（フィールド影響分析）ウィンドウの ViewModel。</summary>
public partial class FieldUsageViewModel : ObservableObject
{
    private readonly FieldUsageService _service;
    private readonly ToolLauncherService _toolLauncher;
    private readonly IFilePickerService _filePicker;
    private readonly AppLog _log;

    private OrgInfo? _org;
    private string _objectApiName = "";
    private string _fieldApiName = "";
    private FieldUsageResult? _result;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _headerText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _warningText = "";

    [ObservableProperty]
    private bool _hasWarning;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private FieldUsageSourceChoice? _selectedSource;

    [ObservableProperty]
    private FieldUsageRow? _selectedRow;

    public ObservableCollection<FieldUsageRow> Hits { get; } = new();

    public IReadOnlyList<FieldUsageSourceChoice> SourceChoices { get; }

    public FieldUsageViewModel(FieldUsageService service, ToolLauncherService toolLauncher, IFilePickerService filePicker, AppLog log)
    {
        _service = service;
        _toolLauncher = toolLauncher;
        _filePicker = filePicker;
        _log = log;
        SourceChoices = new List<FieldUsageSourceChoice>
        {
            new(null, UiText.T("FieldUsage_FilterAll")),
            new(FieldUsageSourceKind.ApexClass, SourceTextFor(FieldUsageSourceKind.ApexClass)),
            new(FieldUsageSourceKind.ApexTrigger, SourceTextFor(FieldUsageSourceKind.ApexTrigger)),
            new(FieldUsageSourceKind.Flow, SourceTextFor(FieldUsageSourceKind.Flow)),
            new(FieldUsageSourceKind.ValidationRule, SourceTextFor(FieldUsageSourceKind.ValidationRule)),
            new(FieldUsageSourceKind.Layout, SourceTextFor(FieldUsageSourceKind.Layout)),
            new(FieldUsageSourceKind.FormulaField, SourceTextFor(FieldUsageSourceKind.FormulaField)),
            new(FieldUsageSourceKind.Permission, SourceTextFor(FieldUsageSourceKind.Permission)),
        };
        SelectedSource = SourceChoices[0];
    }

    /// <summary>対象項目を設定して検索を開始する（ウィンドウ表示前に呼ぶ）。</summary>
    public void Initialize(OrgInfo org, string objectApiName, string fieldApiName, string? fieldLabel)
    {
        _org = org;
        _objectApiName = objectApiName;
        _fieldApiName = fieldApiName;
        Title = UiText.T("FieldUsage_TitleFmt", $"{objectApiName}.{fieldApiName}");
        HeaderText = UiText.T("FieldUsage_HeaderFmt", org.DisplayName, $"{objectApiName}.{fieldApiName}")
            + (string.IsNullOrWhiteSpace(fieldLabel) ? string.Empty : $" ({fieldLabel})");
        _ = RunAsync();
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (_org is not { } org)
        {
            return;
        }

        StatusText = UiText.T("FieldUsage_FindingFmt");
        HasWarning = false;
        WarningText = string.Empty;
        try
        {
            var target = string.IsNullOrWhiteSpace(org.Alias) ? org.Username : org.Alias;
            var result = await _service.AnalyzeAsync(target, _objectApiName, _fieldApiName, org.InstanceUrl);
            _result = result;
            ApplyFilter();

            var sourceCount = result.Hits.Select(h => h.SourceKind).Distinct().Count();
            StatusText = result.TotalCount == 0
                ? UiText.T("FieldUsage_NotFound")
                : UiText.T("FieldUsage_FoundFmt", result.TotalCount, sourceCount, result.Duration.TotalSeconds);

            if (result.Warnings.Count > 0)
            {
                HasWarning = true;
                WarningText = UiText.T(
                    "FieldUsage_WarningFmt",
                    string.Join(" / ", result.Warnings.Select(w => $"{SourceTextFor(w.SourceKind)}: {w.Message}")));
            }
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("FieldUsage_FailedFmt", ex.Message);
            _log.Error("フィールド使用箇所の検索に失敗", ex);
        }
    }

    /// <summary>検索 + ソースで結果を絞り込む。</summary>
    private void ApplyFilter()
    {
        Hits.Clear();
        if (_result is null)
        {
            return;
        }

        var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var source = SelectedSource?.Value;
        foreach (var hit in _result.Hits)
        {
            if (source is { } kind && hit.SourceKind != kind)
            {
                continue;
            }

            if (search is not null
                && !hit.ComponentName.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !hit.Excerpt.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !(hit.Path ?? "").Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Hits.Add(new FieldUsageRow(
                SourceTextFor(hit.SourceKind),
                hit.ComponentName,
                hit.LineNumber is { } line ? UiText.T("FieldUsage_LineFmt", line) : hit.Path ?? "",
                hit.Excerpt,
                hit.OpenUrl));
        }
    }

    /// <summary>行の Salesforce リンクをブラウザーで開く（ダブルクリック）。</summary>
    [RelayCommand]
    private void OpenRow(FieldUsageRow? row)
    {
        if (row?.Url is not { Length: > 0 } url)
        {
            StatusText = UiText.T("FieldUsage_NoLink");
            return;
        }

        var result = _toolLauncher.LaunchBrowser(url);
        if (!result.Success)
        {
            StatusText = result.Message;
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var rows = Hits.ToList();
        if (rows.Count == 0)
        {
            StatusText = UiText.T("FieldUsage_NotFound");
            return;
        }

        var fileName = await _filePicker.SaveFileAsync(
            UiText.T("FieldUsage_SaveTitle"),
            $"field-usage-{_objectApiName}-{_fieldApiName}-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            UiText.T("FieldUsage_SaveFilter"));
        if (fileName is null)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(
                ',',
                CsvExporter.Escape(UiText.T("FieldUsage_ColSource")),
                CsvExporter.Escape(UiText.T("FieldUsage_ColComponent")),
                CsvExporter.Escape(UiText.T("FieldUsage_ColLocation")),
                CsvExporter.Escape(UiText.T("FieldUsage_ColExcerpt")),
                CsvExporter.Escape("URL")));
            foreach (var row in rows)
            {
                builder.AppendLine(string.Join(
                    ',',
                    CsvExporter.Escape(row.SourceText),
                    CsvExporter.Escape(row.ComponentName),
                    CsvExporter.Escape(row.LocationText),
                    CsvExporter.Escape(row.Excerpt),
                    CsvExporter.Escape(row.Url ?? "")));
            }

            await File.WriteAllTextAsync(fileName, builder.ToString(), new UTF8Encoding(true));
            StatusText = UiText.T("FieldUsage_ExportedFmt", fileName);
            _log.Info($"使用箇所を CSV 出力: {fileName} ({rows.Count} 件)");
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Common_SaveFailedFmt", ex.Message);
            _log.Error("使用箇所 CSV の出力に失敗", ex);
        }
    }

    private static string SourceTextFor(FieldUsageSourceKind kind) => kind switch
    {
        FieldUsageSourceKind.ApexClass => UiText.T("FieldUsage_Src_ApexClass"),
        FieldUsageSourceKind.ApexTrigger => UiText.T("FieldUsage_Src_ApexTrigger"),
        FieldUsageSourceKind.Flow => UiText.T("FieldUsage_Src_Flow"),
        FieldUsageSourceKind.ValidationRule => UiText.T("FieldUsage_Src_ValidationRule"),
        FieldUsageSourceKind.Layout => UiText.T("FieldUsage_Src_Layout"),
        FieldUsageSourceKind.FormulaField => UiText.T("FieldUsage_Src_FormulaField"),
        _ => UiText.T("FieldUsage_Src_Permission"),
    };

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedSourceChanged(FieldUsageSourceChoice? value) => ApplyFilter();
}

/// <summary>使用箇所一覧の 1 行。</summary>
public sealed record FieldUsageRow(string SourceText, string ComponentName, string LocationText, string Excerpt, string? Url);

/// <summary>ソース フィルタの選択肢（Value = null はすべて）。</summary>
public sealed record FieldUsageSourceChoice(FieldUsageSourceKind? Value, string Label)
{
    public override string ToString() => Label;
}
