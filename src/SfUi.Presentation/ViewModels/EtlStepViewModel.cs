using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Etl.Connections;
using SfUi.Etl.Engine;
using SfUi.Etl.Expressions;
using SfUi.Etl.Sources;
using SfUi.Etl.Staging;
using SfUi.Etl.Targets;
using SfUi.Etl.Transforms;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>
/// ETL ジョブの 1 ステップ分のエディタ状態（ソース / マッピング / 出力）。
/// 実際の変換は <see cref="CreateSource"/> / <see cref="CreateMapper"/> / <see cref="CreateTarget"/> で
/// SfUi.Etl の部品を組み立てて実行側（EtlStepRun / EtlJobRunner）へ渡す。
/// </summary>
public sealed partial class EtlStepViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _files;
    private readonly AppPaths _paths;
    private readonly Action<string> _status;

    public EtlStepViewModel(
        string stepId,
        IDialogService dialogs,
        IFilePickerService files,
        AppPaths paths,
        Action<string> status)
    {
        StepId = stepId;
        _dialogs = dialogs;
        _files = files;
        _paths = paths;
        _status = status;
    }

    /// <summary>ステップ Id（journal / crosswalk の単位）。</summary>
    public string StepId { get; }

    public string DisplayName => StepId + ": " + EffectiveObjectName;

    public override string ToString() => DisplayName;

    /// <summary>出力オブジェクト名（未入力時は Output）。</summary>
    public string EffectiveObjectName => string.IsNullOrWhiteSpace(ObjectApiName) ? "Output" : ObjectApiName.Trim();

    public IReadOnlyList<string> SourceTypeOptions { get; } = new[] { "CSV", "TSV", "Excel", "JSON", "XML" };

    [ObservableProperty]
    private string _selectedSourceType = "CSV";

    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private bool _sourceHasHeader = true;

    [ObservableProperty]
    private string _excelSheet = string.Empty;

    [ObservableProperty]
    private string _xmlRowElement = string.Empty;

    public ObservableCollection<EtlMappingRow> Mappings { get; } = new();

    public IReadOnlyList<string> TargetTypeOptions { get; } = new[] { "Salesforce", "CSV" };

    [ObservableProperty]
    private string _selectedTargetType = "Salesforce";

    [ObservableProperty]
    private string _objectApiName = "Account";

    public IReadOnlyList<string> OpOptions { get; } = new[] { RowOp.Insert, RowOp.Update, RowOp.Upsert, RowOp.Delete };

    [ObservableProperty]
    private string _selectedOp = RowOp.Insert;

    [ObservableProperty]
    private string _matchKeyField = string.Empty;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    /// <summary>crosswalk に source_key として記録するターゲット項目名（親の Id を子の LOOKUP で参照する）。</summary>
    [ObservableProperty]
    private string _crosswalkKeyField = string.Empty;

    partial void OnObjectApiNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        var path = await _files.OpenFileAsync(UiText.T("Etl_SourceGroup"), UiText.T("Etl_SourceFilter"), _paths.DataRoot);
        if (path is not null)
        {
            SourcePath = path;
        }
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var path = await _files.SaveFileAsync(UiText.T("Etl_OutputPath"), EffectiveObjectName + ".csv", UiText.T("Etl_OutputFilter"), _paths.DataRoot);
        if (path is not null)
        {
            OutputPath = path;
        }
    }

    [RelayCommand]
    private void LoadSource()
    {
        try
        {
            var source = CreateSource();
            Mappings.Clear();
            foreach (var column in source.Columns)
            {
                Mappings.Add(new EtlMappingRow(column));
            }

            _status(UiText.T("Etl_SourceLoadedFmt", Mappings.Count));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _dialogs.Warning(ex.Message, UiText.T("Etl_Title"));
            _status(UiText.T("Common_FailedFmt", ex.Message));
        }
    }

    /// <summary>入力ソースを生成する。</summary>
    public IEtlSource CreateSource() => SelectedSourceType switch
    {
        "CSV" => new CsvFileSource(SourcePath, new CsvStreamOptions { HasHeader = SourceHasHeader }),
        "TSV" => new CsvFileSource(SourcePath, new CsvStreamOptions { Delimiter = '\t', HasHeader = SourceHasHeader }),
        "Excel" => new ExcelFileSource(SourcePath, string.IsNullOrWhiteSpace(ExcelSheet) ? null : ExcelSheet, SourceHasHeader),
        "JSON" => new JsonFileSource(SourcePath),
        "XML" => new XmlFileSource(SourcePath, string.IsNullOrWhiteSpace(XmlRowElement) ? null : XmlRowElement),
        _ => throw new InvalidOperationException("未対応の入力種別です: " + SelectedSourceType),
    };

    /// <summary>マッピング変換器を生成する（式エンジンは LOOKUP 配線済みの共有インスタンスを渡す）。</summary>
    public RowMapper CreateMapper(ExpressionEngine engine)
    {
        var mappings = Mappings
            .Select(m => new FieldMapping(m.TargetField, m.Expression, m.Type))
            .ToList();
        return new RowMapper(Mappings.Select(m => m.SourceColumn).ToList(), mappings, engine);
    }

    /// <summary>出力ターゲットを生成する（Salesforce は巻き戻し対応）。</summary>
    public (IEtlTarget Target, IEtlRevertable? Revertable) CreateTarget(
        SalesforceRestClient rest,
        string targetOrg,
        AppLog log)
    {
        var fields = Mappings.Select(m => m.TargetField).ToList();
        if (SelectedTargetType == "CSV")
        {
            return (new CsvFileTarget(OutputPath, fields), null);
        }

        var target = new SalesforceTarget(rest, targetOrg, new SalesforceTargetOptions
        {
            ObjectName = EffectiveObjectName,
            Fields = fields,
            Op = SelectedOp,
            MatchKeyField = string.IsNullOrWhiteSpace(MatchKeyField) ? null : MatchKeyField.Trim(),
        }, log);
        return (target, target);
    }
}
