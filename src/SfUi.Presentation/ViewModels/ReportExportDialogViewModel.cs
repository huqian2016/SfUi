using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>
/// レポート出力ダイアログ（出力形式・分析・行数・出力先）の ViewModel。
/// WPF / Avalonia の両方のダイアログから使う。
/// </summary>
public sealed partial class ReportExportDialogViewModel : ObservableObject
{
    private readonly IFilePickerService _filePicker;
    private readonly ExportSettingsStore? _settings;

    [ObservableProperty]
    private bool _csv = true;

    [ObservableProperty]
    private bool _markdown;

    [ObservableProperty]
    private bool _html;

    [ObservableProperty]
    private bool _excel;

    [ObservableProperty]
    private bool _pdf;

    [ObservableProperty]
    private bool _includeAnalysis = true;

    [ObservableProperty]
    private string _maxRowsText = "5000";

    [ObservableProperty]
    private string _baseName;

    [ObservableProperty]
    private string _outputDirectory;

    [ObservableProperty]
    private bool _openAfterSave = true;

    [ObservableProperty]
    private string? _errorText;

    public ReportExportDialogViewModel(
        ReportDocument document,
        string suggestedBaseName,
        string? initialDirectory,
        IFilePickerService filePicker,
        ExportSettingsStore? settings = null)
    {
        _filePicker = filePicker;
        _settings = settings;
        Document = document;
        _baseName = suggestedBaseName;
        var defaults = settings?.LoadReport();
        _outputDirectory = !string.IsNullOrWhiteSpace(initialDirectory)
            ? initialDirectory!
            : defaults?.OutputDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (defaults is not null)
        {
            Csv = defaults.Csv;
            Markdown = defaults.Markdown;
            Html = defaults.Html;
            Excel = defaults.Excel;
            Pdf = defaults.Pdf;
            IncludeAnalysis = defaults.IncludeAnalysis;
            MaxRowsText = defaults.MaxRows.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            OpenAfterSave = defaults.OpenAfterSave;
        }

        Sections.Add($"{UiText.T("Report_MetaTitle")}: {document.Title}");
        if (!string.IsNullOrWhiteSpace(document.Org))
        {
            Sections.Add($"{UiText.T("Report_MetaOrg")}: {document.Org}");
        }

        Sections.Add($"{UiText.T("Report_MetaGenerated")}: {document.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        foreach (var table in document.Tables)
        {
            Sections.Add($"{table.Name} ({table.RowCount:N0} x {table.Columns.Count})");
        }

        var bulletSections = document.Sections.Where(section => section.Bullets is { Count: > 0 }).ToList();
        foreach (var section in bulletSections)
        {
            Sections.Add($"{section.Heading} ({section.Bullets!.Count})");
        }
    }

    public ReportDocument Document { get; }

    public ObservableCollection<string> Sections { get; } = new();

    public string Title => UiText.T("Report_DialogTitle");

    public string FormatsLabel => UiText.T("Report_Formats");

    public string FormatCsv => UiText.T("Report_FormatCsv");

    public string FormatMarkdown => UiText.T("Report_FormatMarkdown");

    public string FormatHtml => UiText.T("Report_FormatHtml");

    public string FormatExcel => UiText.T("Report_FormatExcel");

    public string FormatPdf => UiText.T("Report_FormatPdf");

    public string IncludeAnalysisLabel => UiText.T("Report_IncludeAnalysis");

    public string MaxRowsLabel => UiText.T("Report_MaxRowsLabel");

    public string OutputFolderLabel => UiText.T("Report_OutputFolder");

    public string FileNameLabel => UiText.T("Report_FileName");

    public string OpenAfterSaveLabel => UiText.T("Report_OpenAfterSave");

    public string ContentsLabel => UiText.T("Report_Contents");

    public string ExportLabel => UiText.T("Report_Export");

    public string CancelLabel => UiText.T("Common_Cancel");

    public string BrowseLabel => UiText.T("Report_Browse");

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await _filePicker.PickFolderAsync(UiText.T("Report_OutputFolder"), OutputDirectory);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            OutputDirectory = folder;
        }
    }

    /// <summary>入力内容から出力設定を作る（形式未選択・出力先未指定なら null）。</summary>
    public ReportExportOptions? BuildOptions()
    {
        var formats = ReportFormats.None;
        if (Csv)
        {
            formats |= ReportFormats.Csv;
        }

        if (Markdown)
        {
            formats |= ReportFormats.Markdown;
        }

        if (Html)
        {
            formats |= ReportFormats.Html;
        }

        if (Excel)
        {
            formats |= ReportFormats.Excel;
        }

        if (Pdf)
        {
            formats |= ReportFormats.Pdf;
        }

        if (formats == ReportFormats.None)
        {
            ErrorText = UiText.T("Report_NoFormats");
            return null;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            ErrorText = UiText.T("Report_OutputFolder");
            return null;
        }

        var maxRows = 5000;
        if (!string.IsNullOrWhiteSpace(MaxRowsText))
        {
            if (!int.TryParse(MaxRowsText.Trim(), out maxRows) || maxRows < 0)
            {
                ErrorText = UiText.T("Report_MaxRowsLabel");
                return null;
            }
        }

        ErrorText = null;

        // 次回同じ設定で開けるよう保存する
        SaveDefaults(formats, maxRows);

        return new ReportExportOptions
        {
            Formats = formats,
            IncludeAnalysis = IncludeAnalysis,
            MaxRows = maxRows,
            OutputDirectory = OutputDirectory.Trim(),
            BaseName = string.IsNullOrWhiteSpace(BaseName) ? "report" : BaseName.Trim(),
            OpenAfterSave = OpenAfterSave,
        };
    }

    /// <summary>今の設定を保存する（次回起動時に復元する）。</summary>
    private void SaveDefaults(ReportFormats formats, int maxRows)
    {
        if (_settings is null)
        {
            return;
        }

        try
        {
            _settings.SaveReport(new ReportExportDefaults
            {
                Csv = formats.HasFlag(ReportFormats.Csv),
                Markdown = formats.HasFlag(ReportFormats.Markdown),
                Html = formats.HasFlag(ReportFormats.Html),
                Excel = formats.HasFlag(ReportFormats.Excel),
                Pdf = formats.HasFlag(ReportFormats.Pdf),
                IncludeAnalysis = IncludeAnalysis,
                MaxRows = maxRows,
                OpenAfterSave = OpenAfterSave,
                OutputDirectory = string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory.Trim(),
            });
        }
        catch
        {
            // 設定の保存に失敗しても出力自体は続行する
        }
    }

    /// <summary>形式ボタン（CSV など）をまとめて切り替える。主にテスト・既定値用。</summary>
    public void SetFormats(ReportFormats formats)
    {
        Csv = formats.HasFlag(ReportFormats.Csv);
        Markdown = formats.HasFlag(ReportFormats.Markdown);
        Html = formats.HasFlag(ReportFormats.Html);
        Excel = formats.HasFlag(ReportFormats.Excel);
        Pdf = formats.HasFlag(ReportFormats.Pdf);
    }
}
