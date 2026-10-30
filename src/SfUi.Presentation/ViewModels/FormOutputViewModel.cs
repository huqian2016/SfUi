using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>帳票の差し込み値 1 件。</summary>
public sealed partial class FormValueItem : ObservableObject
{
    public FormValueItem(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>差し込みキー（{Values:Key}）。</summary>
    public string Key { get; }

    /// <summary>画面表示用（キーを単語に分けたもの）。</summary>
    public string Label { get; }

    [ObservableProperty]
    private string _value = "";
}

/// <summary>
/// 帳票テンプレート出力ダイアログの ViewModel。
/// テンプレート選択 → 差し込み値の入力 → プレビュー → 出力（HTML / PDF / SVG / PNG / JPEG）。
/// </summary>
public sealed partial class FormOutputViewModel : ObservableObject
{
    private static readonly Regex PlaceholderPattern = new(@"\{(?:Values:)?([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    private static readonly HashSet<string> BuiltInKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Title", "ReportName", "Org", "Source", "Date", "DateTime", "Rows", "Page", "Pages",
    };

    private readonly FormStore _store;
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private ReportDocument? _document;
    private bool _loading;

    public FormOutputViewModel(FormStore store, AppPaths paths, AppLog log)
    {
        _store = store;
        _paths = paths;
        _log = log;
    }

    /// <summary>選択できる帳票（組み込み + ユーザー定義）。</summary>
    public ObservableCollection<FormDefinition> Forms { get; } = new();

    /// <summary>地域の選択肢（先頭は「すべて」）。</summary>
    public IReadOnlyList<ReportChoice> Regions { get; } = new[]
        {
            new ReportChoice("", UiText.T("Form_RegionAll")),
        }
        .Concat(FormRegions.All.Select(FormRegions.ToChoice))
        .ToList();

    /// <summary>差し込み値。</summary>
    public ObservableCollection<FormValueItem> Values { get; } = new();

    [ObservableProperty]
    private FormDefinition? _selectedForm;

    [ObservableProperty]
    private ReportChoice? _selectedRegion;

    [ObservableProperty]
    private byte[]? _previewPng;

    [ObservableProperty]
    private string _outputName = "";

    [ObservableProperty]
    private bool _writeHtml = true;

    [ObservableProperty]
    private bool _writePdf = true;

    [ObservableProperty]
    private bool _writeSvg;

    [ObservableProperty]
    private bool _writePng;

    [ObservableProperty]
    private bool _writeJpeg;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _categoryText = "";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>プレビューがあるか。</summary>
    public bool HasPreview => PreviewPng is { Length: > 0 };

    /// <summary>前回出力したファイル。</summary>
    public IReadOnlyList<string> LastFiles { get; private set; } = Array.Empty<string>();

    // ── 表示用ラベル ──
    public string Title => UiText.T("Form_OutputTitle");

    public string LabelTemplate => UiText.T("Form_LabelTemplate");

    public string LabelRegion => UiText.T("Form_LabelRegion");

    public string LabelValues => UiText.T("Form_LabelValues");

    public string LabelFormats => UiText.T("Form_LabelFormats");

    public string LabelOutput => UiText.T("Form_LabelOutput");

    public string LabelRender => UiText.T("Form_Render");

    public string LabelPrint => UiText.T("Form_Print");

    public string LabelCancel => UiText.T("Common_Cancel");

    /// <summary>出力先（常に data/exports）。</summary>
    public string OutputHint => UiText.T("Sched_ExportPathHintFmt", Path.Combine(_paths.ExportsDirectory, OutputName + ".pdf"));

    partial void OnPreviewPngChanged(byte[]? value) => OnPropertyChanged(nameof(HasPreview));

    partial void OnOutputNameChanged(string value) => OnPropertyChanged(nameof(OutputHint));

    partial void OnSelectedFormChanged(FormDefinition? value)
    {
        if (_loading)
        {
            return;
        }

        CategoryText = value is null ? "" : FormCategories.ToLabel(value.Category);
        RebuildValues();
        RefreshPreview();
    }

    partial void OnSelectedRegionChanged(ReportChoice? value)
    {
        if (_loading)
        {
            return;
        }

        ApplyRegionFilter();
        SelectedForm = Forms.FirstOrDefault();
    }

    /// <summary>選択中の地域で帳票一覧を作り直す。</summary>
    private void ApplyRegionFilter(string? selectId = null)
    {
        var region = SelectedRegion?.Key ?? "";
        var forms = string.IsNullOrWhiteSpace(region)
            ? _store.AllForms
            : _store.AllForms.Where(f => string.Equals(f.Region, region, StringComparison.OrdinalIgnoreCase)).ToList();

        Forms.Clear();
        foreach (var form in forms)
        {
            Forms.Add(form);
        }

        if (!string.IsNullOrWhiteSpace(selectId))
        {
            SelectedForm = Forms.FirstOrDefault(f => f.Id == selectId) ?? Forms.FirstOrDefault();
        }
    }

    /// <summary>レポート結果を読み込む。</summary>
    public void Load(ReportDocument document, string suggestedBaseName)
    {
        _loading = true;
        try
        {
            _document = document;
            OutputName = suggestedBaseName;

            SelectedRegion = Regions[0];
            ApplyRegionFilter();
            SelectedForm = Forms.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }

        CategoryText = SelectedForm is null ? "" : FormCategories.ToLabel(SelectedForm.Category);
        RebuildValues();
        RefreshPreview();
    }

    /// <summary>テンプレートの差し込みキーから入力欄を作り直す。</summary>
    public void RebuildValues()
    {
        var existing = Values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        Values.Clear();
        if (SelectedForm is null)
        {
            return;
        }

        var keys = new List<string>();
        foreach (var text in SelectedForm.Header.Concat(SelectedForm.Footer).Select(e => e.Text))
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            foreach (Match match in PlaceholderPattern.Matches(text))
            {
                var key = match.Groups[1].Value;
                if (BuiltInKeys.Contains(key) || keys.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                keys.Add(key);
            }
        }

        foreach (var key in keys)
        {
            var item = new FormValueItem(key, Humanize(key));
            if (existing.TryGetValue(key, out var value))
            {
                item.Value = value;
            }

            Values.Add(item);
        }
    }

    /// <summary>1 ページ目をプレビュー用 PNG にする。</summary>
    public void RefreshPreview()
    {
        try
        {
            if (SelectedForm is null || _document is null)
            {
                PreviewPng = null;
                return;
            }

            var pages = FormDrawList.Build(SelectedForm, BuildFormData());
            PreviewPng = pages.Count == 0 ? null : FormImageWriter.RenderPage(pages[0], ReportImageFormat.Png, 110);
        }
        catch (Exception ex)
        {
            _log.Warn($"帳票プレビューの作成に失敗しました: {ex.Message}");
            PreviewPng = null;
        }
    }

    /// <summary>選択中の形式で出力する。</summary>
    [RelayCommand]
    private Task RenderAsync() => RenderInternalAsync();

    /// <summary>選択中の形式で出力する。</summary>
    public async Task<ReportExportResult?> RenderInternalAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedForm is null || _document is null)
        {
            StatusText = UiText.T("Reports_NoSelection");
            return null;
        }

        var formats = SelectedFormatFlags();
        if (formats == ReportFormats.None)
        {
            StatusText = UiText.T("Report_NoFormats");
            return null;
        }

        try
        {
            IsBusy = true;
            StatusText = UiText.T("Report_Exporting");
            var result = await FormOutputWriter.WriteAsync(
                SelectedForm,
                BuildFormData(),
                formats,
                _paths.ExportsDirectory,
                string.IsNullOrWhiteSpace(OutputName) ? "form" : OutputName.Trim(),
                cancellationToken: cancellationToken).ConfigureAwait(true);

            LastFiles = result.Files;
            StatusText = UiText.T("Report_DoneFmt", result.Files.Count, _paths.ExportsDirectory);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warn($"帳票出力に失敗しました: {ex.Message}");
            StatusText = UiText.T("Report_FailedFmt", ex.Message);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>出力対象の形式。</summary>
    public ReportFormats SelectedFormatFlags()
    {
        var formats = ReportFormats.None;
        if (WriteHtml)
        {
            formats |= ReportFormats.Html;
        }

        if (WritePdf)
        {
            formats |= ReportFormats.Pdf;
        }

        if (WriteSvg)
        {
            formats |= ReportFormats.Svg;
        }

        if (WritePng)
        {
            formats |= ReportFormats.Png;
        }

        if (WriteJpeg)
        {
            formats |= ReportFormats.Jpeg;
        }

        return formats;
    }

    /// <summary>差し込みデータを組み立てる。</summary>
    public FormData BuildFormData() => new()
    {
        Title = _document?.Title ?? "",
        Org = _document?.Org,
        Source = _document?.Source,
        GeneratedAt = _document?.GeneratedAt ?? DateTimeOffset.Now,
        Table = _document?.Tables.FirstOrDefault(),
        Values = Values
            .Where(v => !string.IsNullOrWhiteSpace(v.Value))
            .ToDictionary(v => v.Key, v => v.Value.Trim(), StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>印刷用のページ画像（150dpi PNG）。</summary>
    public IReadOnlyList<byte[]> RenderPrintPages()
    {
        if (SelectedForm is null || _document is null)
        {
            return Array.Empty<byte[]>();
        }

        return FormDrawList.Build(SelectedForm, BuildFormData())
            .Select(page => FormImageWriter.RenderPage(page, ReportImageFormat.Png, 150))
            .ToList();
    }

    private static string Humanize(string key)
    {
        var builder = new StringBuilder(key.Length + 4);
        for (var i = 0; i < key.Length; i++)
        {
            var ch = key[i];
            if (i > 0 && char.IsUpper(ch) && !char.IsUpper(key[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private string FormatNumber(double value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
