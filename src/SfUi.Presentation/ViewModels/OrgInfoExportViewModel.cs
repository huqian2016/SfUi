using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>
/// 組織情報ウィンドウ「エクスポート」タブの ViewModel。
/// 選択したオブジェクトの定義書（オブジェクト / 項目 / 画面レイアウト / リストビュー / フロー）を
/// xlsx / CSV として出力する。AI 添付・横断検索の対象外。
/// </summary>
public partial class OrgInfoExportViewModel : ObservableObject, IOrgInfoTab, IDisposable
{
    private readonly OrgInfo _org;
    private readonly string _orgKey;
    private readonly OrgInfoService _service;
    private readonly OrgExportService _export;
    private readonly OrgInfoCacheStore _cache;
    private readonly IFilePickerService _filePicker;
    private readonly ToolLauncherService _toolLauncher;
    private readonly AppLog _log;
    private readonly UiDebouncer _filterDebounce;
    private readonly List<OrgExportObjectItemViewModel> _all = new();
    private readonly List<string> _outputFiles = new();

    private CancellationTokenSource? _cts;
    private int _lastFileCount = -1;
    private int _lastWarningCount;

    [ObservableProperty]
    private string _title = UiText.T("OrgInfo_Tab_Export");

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _customOnly = true;

    [ObservableProperty]
    private bool _exportObjects = true;

    [ObservableProperty]
    private bool _exportFields = true;

    [ObservableProperty]
    private bool _exportLayouts = true;

    [ObservableProperty]
    private bool _exportListViews = true;

    [ObservableProperty]
    private bool _exportFlows = true;

    [ObservableProperty]
    private bool _activeFlowsOnly = true;

    [ObservableProperty]
    private bool _excel = true;

    [ObservableProperty]
    private bool _csv = true;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private bool _isLoadingObjects;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private string _warningText = string.Empty;

    [ObservableProperty]
    private string _selectionSummary = string.Empty;

    /// <summary>フィルター適用後のオブジェクト チェックボックス一覧。</summary>
    public ObservableCollection<OrgExportObjectItemViewModel> Items { get; } = new();

    public bool CanRun => !IsBusy && !IsLoadingObjects;

    public bool HasFiles => _outputFiles.Count > 0;

    public OrgInfoExportViewModel(
        OrgInfo org,
        string orgKey,
        OrgInfoService service,
        OrgExportService export,
        OrgInfoCacheStore cache,
        IFilePickerService filePicker,
        ToolLauncherService toolLauncher,
        IUiDispatcher ui,
        AppLog log)
    {
        _org = org;
        _orgKey = orgKey;
        _service = service;
        _export = export;
        _cache = cache;
        _filePicker = filePicker;
        _toolLauncher = toolLauncher;
        _log = log;
        _filterDebounce = new UiDebouncer(200, ui);
        ReloadObjectCandidates();
    }

    // ---- IOrgInfoTab ----

    public bool HasData => false;

    public DateTimeOffset? FetchedAt => null;

    /// <summary>AI パネルへの添付対象外。</summary>
    public string? BuildAttachmentText(int maxChars) => null;

    public void Relocalize()
    {
        Title = UiText.T("OrgInfo_Tab_Export");
        ApplyFilter();
        UpdateResultText();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _filterDebounce.Cancel();
    }

    // ---- オブジェクト一覧 ----

    partial void OnFilterTextChanged(string value) => _filterDebounce.Debounce(ApplyFilter);

    partial void OnCustomOnlyChanged(bool value) => ApplyFilter();

    /// <summary>キャッシュ済みのオブジェクト一覧から候補を作り直す（選択は API 名で維持）。</summary>
    public void ReloadObjectCandidates()
    {
        var section = _cache.GetSection(_orgKey, OrgInfoSections.Objects);
        ApplySection(section);
    }

    private void ApplySection(OrgInfoSection? section)
    {
        var selected = _all.Where(item => item.IsSelected).Select(item => item.ApiName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _all)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _all.Clear();
        if (section is not null)
        {
            foreach (var row in section.Rows)
            {
                var apiName = row.Get("apiName");
                if (string.IsNullOrWhiteSpace(apiName))
                {
                    continue;
                }

                var item = new OrgExportObjectItemViewModel(apiName!, row.Get("label") ?? string.Empty, IsCustomObject(apiName!, row.Get("kind")))
                {
                    IsSelected = selected.Contains(apiName!),
                };
                item.PropertyChanged += OnItemPropertyChanged;
                _all.Add(item);
            }
        }

        ApplyFilter();
    }

    /// <summary>カスタムオブジェクト判定（kind トークンを優先し、なければ API 名の接尾辞で判定）。</summary>
    private static bool IsCustomObject(string apiName, string? kind)
    {
        if (string.Equals(kind, OrgInfoTokens.Custom, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(kind, OrgInfoTokens.Standard, StringComparison.Ordinal))
        {
            return false;
        }

        return apiName.EndsWith("__c", StringComparison.OrdinalIgnoreCase) ||
            apiName.EndsWith("__b", StringComparison.OrdinalIgnoreCase) ||
            apiName.EndsWith("__x", StringComparison.OrdinalIgnoreCase) ||
            apiName.EndsWith("__e", StringComparison.OrdinalIgnoreCase) ||
            apiName.EndsWith("__mdt", StringComparison.OrdinalIgnoreCase);
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrgExportObjectItemViewModel.IsSelected))
        {
            UpdateSelectionSummary();
        }
    }

    private void ApplyFilter()
    {
        var filter = FilterText.Trim();
        Items.Clear();
        foreach (var item in _all)
        {
            if (CustomOnly && !item.IsCustom)
            {
                continue;
            }

            if (filter.Length > 0 &&
                !item.ApiName.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !item.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Items.Add(item);
        }

        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary() =>
        SelectionSummary = UiText.T("OrgInfoExport_SelectedFmt", _all.Count(item => item.IsSelected));

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Items)
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }
    }

    /// <summary>オブジェクト一覧を API から再取得してキャッシュを更新する。</summary>
    [RelayCommand]
    private async Task RefreshObjectsAsync()
    {
        if (IsLoadingObjects || IsBusy)
        {
            return;
        }

        IsLoadingObjects = true;
        ResultText = string.Empty;
        try
        {
            var section = await _service.FetchObjectsAsync(_org).ConfigureAwait(true);
            _cache.UpsertSection(_orgKey, section);
            ReloadObjectCandidates();
        }
        catch (Exception ex)
        {
            _log.Error("オブジェクト一覧の取得に失敗", ex);
            ResultText = UiText.T("OrgInfoExport_LoadObjectsFailedFmt", ex.Message);
        }
        finally
        {
            IsLoadingObjects = false;
        }
    }

    // ---- 出力先 ----

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var picked = await _filePicker.PickFolderAsync(
            UiText.T("OrgInfoExport_PickFolderTitle"),
            string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory).ConfigureAwait(true);
        if (!string.IsNullOrEmpty(picked))
        {
            OutputDirectory = picked;
        }
    }

    // ---- 実行 ----

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        var directory = _outputFiles.Count > 0 ? Path.GetDirectoryName(_outputFiles[0]) : null;
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            _toolLauncher.LaunchExplorer(directory);
        }
    }

    public bool CanOpenFolder => HasFiles;

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var documents = OrgExportDocuments.None;
        if (ExportObjects)
        {
            documents |= OrgExportDocuments.Objects;
        }

        if (ExportFields)
        {
            documents |= OrgExportDocuments.Fields;
        }

        if (ExportLayouts)
        {
            documents |= OrgExportDocuments.Layouts;
        }

        if (ExportListViews)
        {
            documents |= OrgExportDocuments.ListViews;
        }

        if (ExportFlows)
        {
            documents |= OrgExportDocuments.Flows;
        }

        if (documents == OrgExportDocuments.None)
        {
            ResultText = UiText.T("OrgInfoExport_NoDocs");
            return;
        }

        if (!Excel && !Csv)
        {
            ResultText = UiText.T("OrgInfoExport_NoFormat");
            return;
        }

        var selectedObjects = _all.Where(item => item.IsSelected).Select(item => item.ApiName).ToList();
        if (documents != OrgExportDocuments.Flows && selectedObjects.Count == 0)
        {
            ResultText = UiText.T("OrgInfoExport_NoSelection");
            return;
        }

        IsBusy = true;
        ResultText = string.Empty;
        WarningText = string.Empty;
        ProgressPercent = 0;
        ProgressText = UiText.T("Common_Running");
        _lastFileCount = -1;
        _outputFiles.Clear();
        OnPropertyChanged(nameof(HasFiles));
        OpenFolderCommand.NotifyCanExecuteChanged();
        _cts = new CancellationTokenSource();
        try
        {
            var request = new OrgExportRequest
            {
                ObjectApiNames = selectedObjects,
                Documents = documents,
                ActiveFlowsOnly = ActiveFlowsOnly,
                Excel = Excel,
                Csv = Csv,
                OutputDirectory = OutputDirectory.Trim(),
            };

            var progress = new Progress<OrgExportProgress>(OnProgress);
            var result = await _export.ExportAsync(_org, request, progress, _cts.Token).ConfigureAwait(true);

            _outputFiles.AddRange(result.Files);
            OnPropertyChanged(nameof(HasFiles));
            OpenFolderCommand.NotifyCanExecuteChanged();
            _lastFileCount = result.Files.Count;
            _lastWarningCount = result.Warnings.Count;
            WarningText = string.Join(Environment.NewLine, result.Warnings.Take(10));

            if (result.Files.Count == 0)
            {
                _lastFileCount = -1;
                ResultText = result.Warnings.Count == 0 ? UiText.T("OrgInfoExport_NoData") : UiText.T("Common_FailedFmt", result.Warnings[0]);
            }
            else
            {
                // サービスの既定出力先が使われた場合に画面へ反映する
                var directory = Path.GetDirectoryName(result.Files[0]);
                if (!string.IsNullOrEmpty(directory))
                {
                    OutputDirectory = directory;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _lastFileCount = -1;
            ResultText = UiText.T("Common_Canceled");
        }
        catch (Exception ex)
        {
            _lastFileCount = -1;
            _log.Error("定義書エクスポートに失敗", ex);
            ResultText = UiText.T("Common_FailedFmt", ex.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = 0;
            ProgressText = string.Empty;
            _cts.Dispose();
            _cts = null;
        }

        // IsBusy 解除後に完了文言を反映する（失敗・キャンセル時は _lastFileCount = -1 のため何もしない）
        UpdateResultText();
    }

    private void UpdateResultText()
    {
        if (IsBusy || _lastFileCount < 0)
        {
            return;
        }

        ResultText = _lastWarningCount > 0
            ? UiText.T("OrgInfoExport_DoneWarnFmt", _lastFileCount, _lastWarningCount)
            : UiText.T("OrgInfoExport_DoneFmt", _lastFileCount);
    }

    private void OnProgress(OrgExportProgress progress)
    {
        var stageKey = progress.Stage switch
        {
            OrgExportStage.Objects => "OrgExport_Sheet_Objects",
            OrgExportStage.Fields => "OrgInfoExport_DocFields",
            OrgExportStage.Layouts => "OrgExport_Sheet_Layouts",
            OrgExportStage.ListViews => "OrgExport_Sheet_ListViews",
            OrgExportStage.Flows => "OrgExport_Sheet_Flows",
            _ => "OrgInfoExport_Stage_Writing",
        };
        var detail = string.IsNullOrEmpty(progress.Detail) ? string.Empty : $" — {progress.Detail}";
        ProgressText = UiText.T("OrgInfoExport_ProgressFmt", UiText.T(stageKey), progress.Current, progress.Total) + detail;

        var ratio = progress.Total > 0 ? Math.Clamp((double)progress.Current / progress.Total, 0, 1) : 0;
        ProgressPercent = ((int)progress.Stage + ratio) / 6.0;
    }
}

/// <summary>エクスポート対象オブジェクト 1 件（チェックボックス用）。</summary>
public partial class OrgExportObjectItemViewModel : ObservableObject
{
    public OrgExportObjectItemViewModel(string apiName, string label, bool isCustom)
    {
        ApiName = apiName;
        Label = label;
        IsCustom = isCustom;
        Display = string.IsNullOrWhiteSpace(label) || string.Equals(label, apiName, StringComparison.OrdinalIgnoreCase)
            ? apiName
            : $"{apiName} — {label}";
    }

    public string ApiName { get; }

    public string Label { get; }

    public bool IsCustom { get; }

    public string Display { get; }

    [ObservableProperty]
    private bool _isSelected;
}
