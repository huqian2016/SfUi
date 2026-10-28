using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>帳票レイアウトの選択肢（Id + 表示名）。</summary>
public sealed record ReportLayoutOption(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>用紙の向きの選択肢。</summary>
public sealed record ReportOrientationOption(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// 帳票（SVF 風）出力ダイアログの ViewModel。
/// レイアウトの選択 / 編集（ヘッダー・明細・集計・フッター・用紙）と出力を担当する。
/// </summary>
public sealed partial class ReportFormViewModel : ObservableObject
{
    private readonly ReportLayoutStore _store;
    private readonly AppPaths _paths;
    private readonly IReportRenderer _renderer;
    private ReportDocument? _document;
    private bool _loading;

    public ReportFormViewModel(ReportLayoutStore store, AppPaths paths, IReportRenderer? renderer = null)
    {
        _store = store;
        _paths = paths;
        _renderer = renderer ?? new SvfReportRenderer();
        ReloadLayouts(null);
    }

    /// <summary>選択できるレイアウト。</summary>
    public ObservableCollection<ReportLayoutOption> LayoutOptions { get; } = new();

    /// <summary>用紙サイズ。</summary>
    public IReadOnlyList<string> PaperOptions { get; } = ReportPapers.All;

    /// <summary>用紙の向き。</summary>
    public IReadOnlyList<ReportOrientationOption> OrientationOptions { get; } = new[]
    {
        new ReportOrientationOption("portrait", UiText.T("Report_FormPortrait")),
        new ReportOrientationOption("landscape", UiText.T("Report_FormLandscape")),
    };

    public string Title => UiText.T("Report_FormTitle");

    public string LabelLayout => UiText.T("Report_FormLayout");

    public string LabelNew => UiText.T("Report_FormNew");

    public string LabelDelete => UiText.T("Reports_Delete");

    public string LabelSave => UiText.T("Report_FormSave");

    public string LabelRender => UiText.T("Report_FormRender");

    public string LabelName => UiText.T("Report_FormName");

    public string LabelPaper => UiText.T("Report_FormPaper");

    public string LabelOrientation => UiText.T("Report_FormOrientation");

    public string LabelMargin => UiText.T("Report_FormMargin");

    public string LabelHeader => UiText.T("Report_FormHeader");

    public string LabelDetail => UiText.T("Report_FormDetailRow");

    public string LabelSummary => UiText.T("Report_FormSummaryLabel");

    public string LabelFooter => UiText.T("Report_FormFooter");

    public string LabelRepeatHeader => UiText.T("Report_FormRepeatHeader");

    public string LabelPlaceholders => UiText.T("Report_FormPlaceholders");

    public string LabelOutput => UiText.T("Report_FormOutputName");

    public string LabelFormats => UiText.T("Report_Formats");

    public string LabelCancel => UiText.T("Common_Cancel");

    [ObservableProperty]
    private string _selectedLayoutId = "";

    [ObservableProperty]
    private string _layoutName = "";

    [ObservableProperty]
    private string _paperSize = ReportPapers.A4;

    [ObservableProperty]
    private string _orientation = "portrait";

    [ObservableProperty]
    private double _marginMm = 12;

    [ObservableProperty]
    private string _headerText = "";

    [ObservableProperty]
    private double _headerHeightMm = 18;

    [ObservableProperty]
    private string _summaryText = "";

    [ObservableProperty]
    private double _summaryHeightMm = 12;

    [ObservableProperty]
    private string _footerText = "";

    [ObservableProperty]
    private double _footerHeightMm = 10;

    [ObservableProperty]
    private double _detailRowHeightMm = 7;

    [ObservableProperty]
    private bool _repeatHeaderOnPageBreak = true;

    [ObservableProperty]
    private bool _writeHtml = true;

    [ObservableProperty]
    private bool _writePdf;

    [ObservableProperty]
    private string _outputName = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>出力されたファイル（未出力なら空）。</summary>
    public ReportExportResult? Result { get; private set; }

    /// <summary>出力先（常に data/exports）。</summary>
    public string OutputDirectory => _paths.ExportsDirectory;

    /// <summary>出力先の解決後パス表示。</summary>
    public string OutputHint => UiText.T("Sched_ExportPathHintFmt", BuildFilePath());

    /// <summary>出力内容を読み込む。</summary>
    public void Load(ReportDocument document, string suggestedBaseName)
    {
        _document = document;
        OutputName = suggestedBaseName;

        var preferred = _store.DefaultLayoutId;
        ReloadLayouts(preferred);
        if (string.IsNullOrWhiteSpace(SelectedLayoutId) && LayoutOptions.Count > 0)
        {
            SelectedLayoutId = LayoutOptions[0].Id;
        }

        ApplyLayout(_store.Find(SelectedLayoutId));
        OnPropertyChanged(nameof(OutputHint));
    }

    partial void OnSelectedLayoutIdChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        ApplyLayout(_store.Find(value));
    }

    partial void OnOutputNameChanged(string value) => OnPropertyChanged(nameof(OutputHint));

    /// <summary>レイアウトを新規作成する（既定テンプレート）。</summary>
    [RelayCommand]
    private void NewLayout()
    {
        var layout = ReportLayoutStore.CreateDefault(
            UiText.T("Report_FormNewNameFmt", LayoutOptions.Count + 1));
        _store.Upsert(layout);
        ReloadLayouts(layout.Id);
        ApplyLayout(layout);
        StatusText = UiText.T("Common_SavedFmt", layout.Name);
    }

    /// <summary>選択中のレイアウトを削除する。</summary>
    [RelayCommand]
    private void DeleteLayout()
    {
        if (string.IsNullOrWhiteSpace(SelectedLayoutId))
        {
            return;
        }

        _store.Delete(SelectedLayoutId);
        ReloadLayouts(null);
        ApplyLayout(_store.Find(SelectedLayoutId));
        StatusText = UiText.T("Common_SavedFmt", LayoutName);
    }

    /// <summary>編集内容を保存する。</summary>
    [RelayCommand]
    private void SaveLayout()
    {
        var layout = SaveCurrentLayout();
        StatusText = UiText.T("Common_SavedFmt", layout.Name);
    }

    /// <summary>帳票を出力する（成功したら <see cref="Result"/> にファイル一覧が入る）。</summary>
    [RelayCommand]
    private Task RenderAsync() => RenderInternalAsync();

    /// <summary>帳票を出力する。</summary>
    public async Task<ReportExportResult?> RenderInternalAsync(CancellationToken cancellationToken = default)
    {
        if (_document is null)
        {
            StatusText = UiText.T("Reports_NoSelection");
            return null;
        }

        var formats = ReportFormats.None;
        if (WriteHtml)
        {
            formats |= ReportFormats.Html;
        }

        if (WritePdf)
        {
            formats |= ReportFormats.Pdf;
        }

        if (formats == ReportFormats.None)
        {
            StatusText = UiText.T("Report_NoFormats");
            return null;
        }

        var layout = SaveCurrentLayout();
        var baseName = string.IsNullOrWhiteSpace(OutputName) ? layout.Id : OutputName.Trim();
        try
        {
            IsBusy = true;
            StatusText = UiText.T("Report_Exporting");
            var result = await _renderer.RenderAsync(
                _document,
                new ReportExportOptions
                {
                    Formats = formats,
                    IncludeAnalysis = false,
                    MaxRows = 0,
                    OutputDirectory = _paths.ExportsDirectory,
                    BaseName = baseName,
                    Layout = layout,
                },
                cancellationToken).ConfigureAwait(true);

            Result = result;
            StatusText = UiText.T("Report_DoneFmt", result.Files.Count, _paths.ExportsDirectory);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = UiText.T("Report_FailedFmt", ex.Message);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>レイアウト一覧を読み直す。</summary>
    public void ReloadLayouts(string? selectId)
    {
        LayoutOptions.Clear();
        foreach (var layout in _store.Layouts)
        {
            LayoutOptions.Add(new ReportLayoutOption(layout.Id, layout.Name));
        }

        if (LayoutOptions.Count == 0)
        {
            var created = _store.Upsert(ReportLayoutStore.CreateDefault(UiText.T("Report_FormNewNameFmt", 1)));
            LayoutOptions.Add(new ReportLayoutOption(created.Id, created.Name));
        }

        var id = selectId ?? SelectedLayoutId;
        if (LayoutOptions.All(o => o.Id != id))
        {
            id = LayoutOptions[0].Id;
        }

        SelectedLayoutId = id;
    }

    /// <summary>編集中の内容を既定レイアウトとして保存する。</summary>
    public ReportLayoutDefinition SaveCurrentLayout()
    {
        var layout = _store.Find(SelectedLayoutId) ?? new ReportLayoutDefinition();
        layout.Name = string.IsNullOrWhiteSpace(LayoutName) ? UiText.T("Report_FormTitle") : LayoutName.Trim();
        layout.PaperSize = PaperOptions.Contains(PaperSize) ? PaperSize : ReportPapers.A4;
        layout.Orientation = string.Equals(Orientation, "landscape", StringComparison.OrdinalIgnoreCase)
            ? "landscape"
            : "portrait";
        layout.MarginMm = Math.Clamp(MarginMm, 0, 60);
        layout.Header = BuildRegion(HeaderText, HeaderHeightMm, "left");
        layout.Detail = new ReportLayoutRegion
        {
            Text = "",
            HeightMm = DetailRowHeightMm,
            RowHeightMm = Math.Clamp(DetailRowHeightMm, 3, 40),
        };
        layout.Summary = BuildRegion(SummaryText, SummaryHeightMm, "right");
        layout.Footer = BuildRegion(FooterText, FooterHeightMm, "center");
        layout.RepeatHeaderOnPageBreak = RepeatHeaderOnPageBreak;

        _store.Upsert(layout);

        var option = LayoutOptions.FirstOrDefault(o => o.Id == layout.Id);
        if (option is not null && option.Label != layout.Name)
        {
            var index = LayoutOptions.IndexOf(option);
            LayoutOptions[index] = new ReportLayoutOption(layout.Id, layout.Name);
        }

        return layout;
    }

    private void ApplyLayout(ReportLayoutDefinition? layout)
    {
        _loading = true;
        try
        {
            if (layout is null)
            {
                LayoutName = "";
                HeaderText = "";
                SummaryText = "";
                FooterText = "";
            }
            else
            {
                if (SelectedLayoutId != layout.Id)
                {
                    SelectedLayoutId = layout.Id;
                }

                LayoutName = layout.Name;
                PaperSize = layout.PaperSize;
                Orientation = string.Equals(layout.Orientation, "landscape", StringComparison.OrdinalIgnoreCase)
                    ? "landscape"
                    : "portrait";
                MarginMm = layout.MarginMm;
                HeaderText = layout.Header?.Text ?? "";
                HeaderHeightMm = layout.Header?.HeightMm ?? 18;
                SummaryText = layout.Summary?.Text ?? "";
                SummaryHeightMm = layout.Summary?.HeightMm ?? 12;
                FooterText = layout.Footer?.Text ?? "";
                FooterHeightMm = layout.Footer?.HeightMm ?? 10;
                DetailRowHeightMm = layout.Detail?.RowHeightMm is > 0 and var height ? height : 7;
                RepeatHeaderOnPageBreak = layout.RepeatHeaderOnPageBreak;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private static ReportLayoutRegion? BuildRegion(string? text, double heightMm, string align) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : new ReportLayoutRegion { Text = text, HeightMm = Math.Clamp(heightMm, 0, 200), Align = align };

    private string BuildFilePath()
    {
        var name = string.IsNullOrWhiteSpace(OutputName) ? "form" : OutputName.Trim();
        var extension = WritePdf && !WriteHtml ? ".pdf" : ".html";
        return Path.Combine(_paths.ExportsDirectory, name + extension);
    }
}
