using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>デバッグログ解析ウィンドウの ViewModel（概要 / ツリー / イベント）。</summary>
public partial class LogAnalyzerViewModel : ObservableObject
{
    /// <summary>イベント一覧の最大表示件数（表示のみの上限。CSV 出力は全件）。</summary>
    private const int MaxEventRows = 20_000;

    private readonly IFilePickerService _filePicker;
    private readonly AppLog _log;

    private IReadOnlyList<DebugLogEvent> _allEvents = Array.Empty<DebugLogEvent>();

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _headerText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _slowHeaderText = "";

    [ObservableProperty]
    private string _errorsHeaderText = "";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private LogAnalyzerCategoryChoice? _selectedCategory;

    [ObservableProperty]
    private LogAnalyzerEventRow? _selectedEvent;

    [ObservableProperty]
    private string _selectedDetails = "";

    public ObservableCollection<LogAnalyzerStat> SummaryStats { get; } = new();

    public ObservableCollection<LogAnalyzerLimitRow> Limits { get; } = new();

    public ObservableCollection<LogAnalyzerSlowRow> SlowRows { get; } = new();

    public ObservableCollection<string> Errors { get; } = new();

    public ObservableCollection<DebugLogNode> TreeRoots { get; } = new();

    public ObservableCollection<LogAnalyzerEventRow> EventsView { get; } = new();

    public IReadOnlyList<LogAnalyzerCategoryChoice> CategoryChoices { get; }

    public bool HasLimits => Limits.Count > 0;

    public bool HasNoLimits => Limits.Count == 0;

    public bool HasErrors => Errors.Count > 0;

    public bool HasNoErrors => Errors.Count == 0;

    public LogAnalyzerViewModel(IFilePickerService filePicker, AppLog log)
    {
        _filePicker = filePicker;
        _log = log;
        CategoryChoices = new List<LogAnalyzerCategoryChoice>
        {
            new(null, UiText.T("LogAnalyzer_FilterAll")),
            new(DebugLogEventCategory.Execution, CategoryText(DebugLogEventCategory.Execution)),
            new(DebugLogEventCategory.Method, CategoryText(DebugLogEventCategory.Method)),
            new(DebugLogEventCategory.Soql, CategoryText(DebugLogEventCategory.Soql)),
            new(DebugLogEventCategory.Dml, CategoryText(DebugLogEventCategory.Dml)),
            new(DebugLogEventCategory.Callout, CategoryText(DebugLogEventCategory.Callout)),
            new(DebugLogEventCategory.Flow, CategoryText(DebugLogEventCategory.Flow)),
            new(DebugLogEventCategory.Exception, CategoryText(DebugLogEventCategory.Exception)),
            new(DebugLogEventCategory.Other, CategoryText(DebugLogEventCategory.Other)),
        };
        SelectedCategory = CategoryChoices[0];
    }

    /// <summary>解析結果を表示へ反映する（ウィンドウ表示前に呼ぶ）。</summary>
    public void Initialize(DebugLogAnalysis analysis, string orgLabel, string sourceLabel)
    {
        _allEvents = analysis.Events;
        var summary = analysis.Summary;
        var org = string.IsNullOrWhiteSpace(orgLabel) ? "-" : orgLabel;

        Title = UiText.T("LogAnalyzer_TitleFmt", org);
        HeaderText = UiText.T("LogAnalyzer_HeaderFmt", org, sourceLabel, summary.TotalDurationMs);

        SummaryStats.Clear();
        SummaryStats.Add(new LogAnalyzerStat(UiText.T("LogAnalyzer_Duration"), $"{summary.TotalDurationMs:F0} ms"));
        SummaryStats.Add(new LogAnalyzerStat(UiText.T("LogAnalyzer_EventCount"), summary.EventCount.ToString("N0")));
        SummaryStats.Add(new LogAnalyzerStat(UiText.T("LogAnalyzer_SoqlLabel"), UiText.T("LogAnalyzer_StatFmt", summary.SoqlCount, summary.SoqlRows)));
        SummaryStats.Add(new LogAnalyzerStat(UiText.T("LogAnalyzer_DmlLabel"), UiText.T("LogAnalyzer_StatFmt", summary.DmlCount, summary.DmlRows)));
        SummaryStats.Add(new LogAnalyzerStat(UiText.T("LogAnalyzer_CalloutLabel"), summary.CalloutCount.ToString("N0")));
        SummaryStats.Add(new LogAnalyzerStat(UiText.T("LogAnalyzer_ErrorLabel"), summary.Errors.Count.ToString("N0")));

        Limits.Clear();
        foreach (var limit in summary.Limits)
        {
            Limits.Add(new LogAnalyzerLimitRow(
                limit.Namespace,
                limit.Name,
                $"{limit.Used:N0} / {limit.Max:N0}",
                limit.Percent is { } percent ? $"{percent:F1}%" : "-",
                Math.Clamp(limit.Percent ?? 0, 0, 100)));
        }

        SlowHeaderText = UiText.T("LogAnalyzer_SlowHeaderFmt", 15);
        SlowRows.Clear();
        foreach (var node in DebugLogParser.GetSlowestNodes(analysis, 15))
        {
            SlowRows.Add(new LogAnalyzerSlowRow(
                CategoryText(DebugLogParser.ClassifyEventType(node.EventType)),
                node.Label,
                node.DurationMs is { } ms ? $"{ms:F0}" : "-",
                node.Rows?.ToString("N0") ?? ""));
        }

        ErrorsHeaderText = UiText.T("LogAnalyzer_ErrorsHeaderFmt", summary.Errors.Count);
        Errors.Clear();
        foreach (var error in summary.Errors)
        {
            Errors.Add(UiText.T("LogAnalyzer_ErrorLineFmt", error.Type, error.Message, error.LineNumber?.ToString() ?? "-"));
        }

        summary.Root.Label = UiText.T("LogAnalyzer_RootLabel");
        TreeRoots.Clear();
        TreeRoots.Add(summary.Root);

        SelectedEvent = null;
        SelectedDetails = "";
        ApplyFilter();

        OnPropertyChanged(nameof(HasLimits));
        OnPropertyChanged(nameof(HasNoLimits));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasNoErrors));
    }

    /// <summary>検索 + カテゴリでイベント一覧を絞り込む（表示は MaxEventRows 件で打ち切り）。</summary>
    private void ApplyFilter()
    {
        var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var category = SelectedCategory?.Value;

        static bool Matches(DebugLogEvent e, string? search, DebugLogEventCategory? category)
        {
            if (category is { } c && e.Category != c)
            {
                return false;
            }

            if (search is null)
            {
                return true;
            }

            return e.EventType.Contains(search, StringComparison.OrdinalIgnoreCase)
                || e.Summary.Contains(search, StringComparison.OrdinalIgnoreCase)
                || e.Details.Contains(search, StringComparison.OrdinalIgnoreCase)
                || e.LineNumber.ToString().Contains(search, StringComparison.Ordinal);
        }

        var total = 0;
        foreach (var e in _allEvents)
        {
            if (Matches(e, search, category))
            {
                total++;
            }
        }

        EventsView.Clear();
        if (total > 0)
        {
            var shown = 0;
            foreach (var e in _allEvents)
            {
                if (!Matches(e, search, category))
                {
                    continue;
                }

                EventsView.Add(new LogAnalyzerEventRow(
                    e.LineNumber,
                    e.ElapsedMs,
                    e.EventType,
                    CategoryText(e.Category),
                    e.Summary,
                    e.Details));
                if (++shown >= MaxEventRows)
                {
                    break;
                }
            }
        }

        StatusText = total == 0
            ? UiText.T("LogAnalyzer_NoEvents")
            : total > EventsView.Count
                ? UiText.T("LogAnalyzer_TruncatedFmt", MaxEventRows, total)
                : UiText.T("LogAnalyzer_StatusFmt", EventsView.Count, total);
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (_allEvents.Count == 0)
        {
            StatusText = UiText.T("LogAnalyzer_NoEvents");
            return;
        }

        var fileName = await _filePicker.SaveFileAsync(
            UiText.T("LogAnalyzer_SaveTitle"),
            $"log-events-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            UiText.T("LogAnalyzer_SaveFilter"));
        if (fileName is null)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(
                ',',
                CsvExporter.Escape(UiText.T("LogAnalyzer_ColLine")),
                CsvExporter.Escape(UiText.T("LogAnalyzer_ColElapsed")),
                CsvExporter.Escape(UiText.T("LogAnalyzer_ColType")),
                CsvExporter.Escape(UiText.T("LogAnalyzer_ColCategory")),
                CsvExporter.Escape(UiText.T("LogAnalyzer_ColSummary")),
                CsvExporter.Escape(UiText.T("LogAnalyzer_DetailsLabel"))));

            foreach (var e in _allEvents)
            {
                builder.AppendLine(string.Join(
                    ',',
                    CsvExporter.Escape(e.LineNumber.ToString()),
                    CsvExporter.Escape(e.ElapsedMs.ToString("F3")),
                    CsvExporter.Escape(e.EventType),
                    CsvExporter.Escape(CategoryText(e.Category)),
                    CsvExporter.Escape(e.Summary),
                    CsvExporter.Escape(e.Details)));
            }

            await File.WriteAllTextAsync(fileName, builder.ToString(), new UTF8Encoding(true));
            StatusText = UiText.T("LogAnalyzer_ExportedFmt", fileName);
            _log.Info($"ログイベントを CSV 出力: {fileName} ({_allEvents.Count} 件)");
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Common_SaveFailedFmt", ex.Message);
            _log.Error("ログイベント CSV の出力に失敗", ex);
        }
    }

    private static string CategoryText(DebugLogEventCategory category) => category switch
    {
        DebugLogEventCategory.Execution => UiText.T("LogAnalyzer_CatExecution"),
        DebugLogEventCategory.Method => UiText.T("LogAnalyzer_CatMethod"),
        DebugLogEventCategory.Soql => UiText.T("LogAnalyzer_CatSoql"),
        DebugLogEventCategory.Dml => UiText.T("LogAnalyzer_CatDml"),
        DebugLogEventCategory.Callout => UiText.T("LogAnalyzer_CatCallout"),
        DebugLogEventCategory.Flow => UiText.T("LogAnalyzer_CatFlow"),
        DebugLogEventCategory.Exception => UiText.T("LogAnalyzer_CatException"),
        _ => UiText.T("LogAnalyzer_CatOther"),
    };

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedCategoryChanged(LogAnalyzerCategoryChoice? value) => ApplyFilter();

    partial void OnSelectedEventChanged(LogAnalyzerEventRow? value) => SelectedDetails = value?.Details ?? "";
}

/// <summary>概要カード 1 枚。</summary>
public sealed record LogAnalyzerStat(string Label, string Value);

/// <summary>ガバナ制限の 1 行。</summary>
public sealed record LogAnalyzerLimitRow(string NamespaceText, string Name, string UsageText, string PercentText, double PercentValue);

/// <summary>遅い処理の 1 行。</summary>
public sealed record LogAnalyzerSlowRow(string Kind, string Label, string DurationMs, string Rows);

/// <summary>イベント一覧の 1 行。</summary>
public sealed record LogAnalyzerEventRow(int LineNumber, double ElapsedMs, string EventType, string CategoryText, string Summary, string Details);

/// <summary>カテゴリ フィルタの選択肢（Value = null は「すべて」）。</summary>
public sealed record LogAnalyzerCategoryChoice(DebugLogEventCategory? Value, string Label)
{
    public override string ToString() => Label;
}
