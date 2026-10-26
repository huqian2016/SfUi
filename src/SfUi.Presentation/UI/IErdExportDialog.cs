using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.Presentation;

/// <summary>ER 図ダイアログで選ぶオブジェクト 1 行。</summary>
public sealed partial class ErdExportObjectRow : ObservableObject
{
    private readonly Action? _changed;

    public ErdExportObjectRow(ErdObjectInfo info, Action? changed = null)
    {
        _changed = changed;
        ApiName = info.ApiName;
        Label = string.IsNullOrWhiteSpace(info.Label) ? info.ApiName : info.Label;
        IsCustom = info.IsCustom;
        DisplayName = ErdNames.Format(ApiName, Label, ErdNameStyle.LabelAndApiName);
        SearchText = (ApiName + " " + Label).ToLowerInvariant();
    }

    public string ApiName { get; }

    public string Label { get; }

    public bool IsCustom { get; }

    public string DisplayName { get; }

    public string SearchText { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _changed?.Invoke();
}

/// <summary>ER 図ダイアログの結果。</summary>
public sealed record ErdExportDialogResult(
    IReadOnlyList<string> ObjectApiNames,
    ErdBuildOptions BuildOptions,
    string OutputDirectory,
    string BaseName,
    bool Mermaid,
    bool Dbml,
    bool Drawio,
    bool PlantUml,
    bool Dot,
    bool Html,
    bool Svg,
    bool Png,
    bool Jpeg,
    bool Webp,
    bool OpenFolder);

/// <summary>ER 図出力の設定ダイアログ（各 UI プロジェクトが実装する）。</summary>
public interface IErdExportDialog
{
    Task<ErdExportDialogResult?> ShowAsync(
        string org,
        IReadOnlyList<ErdObjectInfo> objects,
        string suggestedBaseName,
        string? initialDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>ER 図出力ダイアログの ViewModel（UI 非依存）。</summary>
public sealed partial class ErdExportDialogViewModel : ObservableObject
{
    private readonly IReadOnlyList<ErdExportObjectRow> _allObjects;
    private readonly IFilePickerService _filePicker;
    private readonly ExportSettingsStore? _settings;

    public ErdExportDialogViewModel(
        string org,
        IReadOnlyList<ErdObjectInfo> objects,
        string suggestedBaseName,
        string? initialDirectory,
        IFilePickerService filePicker,
        ExportSettingsStore? settings = null)
    {
        Org = org;
        _filePicker = filePicker;
        _settings = settings;
        _allObjects = objects.Select(info => new ErdExportObjectRow(info, OnSelectionChanged)).ToList();
        _baseName = suggestedBaseName;
        var defaults = settings?.LoadErd();
        _outputDirectory = !string.IsNullOrWhiteSpace(initialDirectory)
            ? initialDirectory!
            : defaults?.OutputDirectory ?? string.Empty;

        foreach (var row in _allObjects)
        {
            FilteredObjects.Add(row);
        }

        NameStyleOptions = new[]
        {
            UiText.T("Erd_NameStyleLabel"),
            UiText.T("Erd_NameStyleApi"),
            UiText.T("Erd_NameStyleBoth"),
        };
        _nameStyleIndex = 2;

        if (defaults is not null)
        {
            ApplyDefaults(defaults);
        }
    }

    public string Org { get; }

    public string Title => UiText.T("Erd_DialogTitle");

    public ObservableCollection<ErdExportObjectRow> FilteredObjects { get; } = new();

    public IReadOnlyList<string> NameStyleOptions { get; }

    public string ObjectsLabel => UiText.T("Erd_ObjectsLabel");

    public string FilterLabel => UiText.T("Erd_FilterLabel");

    public string NameStyleLabel => UiText.T("Erd_NameStyleLabel");

    public string FormatsLabel => UiText.T("Erd_FormatsLabel");

    public string OutputLabel => UiText.T("Report_OutputFolder");

    public string BrowseLabel => UiText.T("Report_Browse");

    public string FileNameLabel => UiText.T("Report_FileName");

    public string OpenAfterSaveLabel => UiText.T("Report_OpenAfterSave");

    public string ExportLabel => UiText.T("Report_Export");

    public string CancelLabel => UiText.T("Common_Cancel");

    public string SelectAllLabel => UiText.T("Erd_SelectAll");

    public string ClearLabel => UiText.T("Erd_ClearSelection");

    public string IncludeStandardLabel => UiText.T("Erd_IncludeStandard");

    public string IncludeParentsLabel => UiText.T("Erd_IncludeParents");

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private int _nameStyleIndex;

    [ObservableProperty]
    private string _baseName;

    [ObservableProperty]
    private string _outputDirectory;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private bool _includeStandardObjects = true;

    [ObservableProperty]
    private bool _includeReferencedParents = true;

    [ObservableProperty]
    private bool _openAfterSave;

    [ObservableProperty]
    private bool _mermaid = true;

    [ObservableProperty]
    private bool _dbml;

    [ObservableProperty]
    private bool _drawio;

    [ObservableProperty]
    private bool _plantUml;

    [ObservableProperty]
    private bool _dot;

    [ObservableProperty]
    private bool _html;

    [ObservableProperty]
    private bool _svg = true;

    [ObservableProperty]
    private bool _png;

    [ObservableProperty]
    private bool _jpeg;

    [ObservableProperty]
    private bool _webp;

    public int SelectedCount => _allObjects.Count(row => row.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    /// <summary>選択中の件数（例: 12 件選択中）。</summary>
    public string SelectedCountText => UiText.T("Erd_SelectedCountFmt", SelectedCount);

    partial void OnFilterTextChanged(string value)
    {
        var filter = value?.Trim().ToLowerInvariant() ?? string.Empty;
        FilteredObjects.Clear();
        foreach (var row in _allObjects.Where(row => filter.Length == 0 || row.SearchText.Contains(filter, StringComparison.Ordinal)))
        {
            FilteredObjects.Add(row);
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in FilteredObjects)
        {
            row.IsSelected = true;
        }

        OnSelectionChanged();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        // 絞り込み中は表示されているオブジェクトだけを解除する（全選択と対称）
        foreach (var row in FilteredObjects.ToList())
        {
            row.IsSelected = false;
        }

        OnSelectionChanged();
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await _filePicker.PickFolderAsync(UiText.T("Report_OutputFolder"), string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            OutputDirectory = folder!;
        }
    }

    /// <summary>選択状態が変わったら件数表示を更新する（ビューから呼ぶ）。</summary>
    public void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedCountText));
    }

    /// <summary>入力内容を検証して結果を作る（無効なら ErrorText を設定して null）。</summary>
    public ErdExportDialogResult? BuildResult()
    {
        ErrorText = null;
        var selected = _allObjects.Where(row => row.IsSelected).Select(row => row.ApiName).ToList();
        if (selected.Count == 0)
        {
            ErrorText = UiText.T("Erd_NoObjectsSelected");
            return null;
        }

        if (!Mermaid && !Dbml && !Drawio && !PlantUml && !Dot && !Html && !Svg && !Png && !Jpeg && !Webp)
        {
            ErrorText = UiText.T("Erd_NoFormats");
            return null;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            ErrorText = UiText.T("Erd_NoFolder");
            return null;
        }

        if (string.IsNullOrWhiteSpace(BaseName))
        {
            ErrorText = UiText.T("Report_FileName") + "?";
            return null;
        }

        var style = NameStyleIndex switch
        {
            0 => ErdNameStyle.Label,
            1 => ErdNameStyle.ApiName,
            _ => ErdNameStyle.LabelAndApiName,
        };

        // 次回同じ設定で開けるよう保存する
        SaveDefaults();

        return new ErdExportDialogResult(
            selected,
            new ErdBuildOptions
            {
                NameStyle = style,
                IncludeStandardObjects = IncludeStandardObjects,
                IncludeReferencedParents = IncludeReferencedParents,
            },
            OutputDirectory,
            BaseName,
            Mermaid,
            Dbml,
            Drawio,
            PlantUml,
            Dot,
            Html,
            Svg,
            Png,
            Jpeg,
            Webp,
            OpenAfterSave);
    }

    /// <summary>前回の設定をダイアログへ反映する。</summary>
    private void ApplyDefaults(ErdExportDefaults defaults)
    {
        _nameStyleIndex = defaults.NameStyle switch
        {
            ErdNameStyle.Label => 0,
            ErdNameStyle.ApiName => 1,
            _ => 2,
        };
        IncludeStandardObjects = defaults.IncludeStandardObjects;
        IncludeReferencedParents = defaults.IncludeReferencedParents;
        Mermaid = defaults.Mermaid;
        Dbml = defaults.Dbml;
        Drawio = defaults.Drawio;
        PlantUml = defaults.PlantUml;
        Dot = defaults.Dot;
        Html = defaults.Html;
        Svg = defaults.Svg;
        Png = defaults.Png;
        Jpeg = defaults.Jpeg;
        Webp = defaults.Webp;
        OpenAfterSave = defaults.OpenAfterSave;
    }

    /// <summary>今の設定を保存する（次回起動時に復元する）。</summary>
    private void SaveDefaults()
    {
        if (_settings is null)
        {
            return;
        }

        try
        {
            _settings.SaveErd(new ErdExportDefaults
            {
                NameStyle = NameStyleIndex switch
                {
                    0 => ErdNameStyle.Label,
                    1 => ErdNameStyle.ApiName,
                    _ => ErdNameStyle.LabelAndApiName,
                },
                IncludeStandardObjects = IncludeStandardObjects,
                IncludeReferencedParents = IncludeReferencedParents,
                Mermaid = Mermaid,
                Dbml = Dbml,
                Drawio = Drawio,
                PlantUml = PlantUml,
                Dot = Dot,
                Html = Html,
                Svg = Svg,
                Png = Png,
                Jpeg = Jpeg,
                Webp = Webp,
                OpenAfterSave = OpenAfterSave,
                OutputDirectory = string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory.Trim(),
            });
        }
        catch
        {
            // 設定の保存に失敗しても出力自体は続行する
        }
    }
}
