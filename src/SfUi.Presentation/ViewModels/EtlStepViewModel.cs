using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Etl.Connections;
using SfUi.Etl.Database;
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
    private readonly SalesforceRestClient _rest;
    private readonly Func<string> _targetOrg;

    public EtlStepViewModel(
        string stepId,
        IDialogService dialogs,
        IFilePickerService files,
        AppPaths paths,
        Action<string> status,
        SalesforceRestClient rest,
        Func<string> targetOrg)
    {
        StepId = stepId;
        _dialogs = dialogs;
        _files = files;
        _paths = paths;
        _status = status;
        _rest = rest;
        _targetOrg = targetOrg;
    }

    /// <summary>ステップ Id（journal / crosswalk の単位）。</summary>
    public string StepId { get; }

    public string DisplayName => StepId + ": " + EffectiveObjectName;

    public override string ToString() => DisplayName;

    /// <summary>出力オブジェクト名（未入力時は Output）。</summary>
    public string EffectiveObjectName => string.IsNullOrWhiteSpace(ObjectApiName) ? "Output" : ObjectApiName.Trim();

    public IReadOnlyList<string> SourceTypeOptions { get; } = new[] { "CSV", "TSV", "Excel", "JSON", "XML", "Database", "REST", "Salesforce" };

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

    public IReadOnlyList<string> TargetTypeOptions { get; } = new[] { "Salesforce", "CSV", "Database" };

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

    /// <summary>DB プロバイダーの選択肢（Sqlite / SqlServer / PostgreSql / Odbc）。</summary>
    public IReadOnlyList<string> DbProviderOptions { get; } = DbConnectionSpec.All.Select(k => k.ToString()).ToList();

    // ---- 入力（Database 選択時） ----

    [ObservableProperty]
    private string _sourceDbProvider = DbProviderKind.Sqlite.ToString();

    [ObservableProperty]
    private string _sourceDbConnectionString = string.Empty;

    [ObservableProperty]
    private string _sourceDbQuery = string.Empty;

    // ---- 入力（REST 選択時） ----

    /// <summary>REST 認証方式の選択肢（None / Bearer / Basic / Header）。</summary>
    public IReadOnlyList<string> RestAuthOptions { get; } = new[] { "None", "Bearer", "Basic", "Header" };

    /// <summary>REST ページング方式の選択肢（None / Offset / Link / Cursor）。</summary>
    public IReadOnlyList<string> RestPagingOptions { get; } = new[] { "None", "Offset", "Link", "Cursor" };

    [ObservableProperty]
    private string _sourceRestUrl = string.Empty;

    [ObservableProperty]
    private string _sourceRestAuth = "None";

    [ObservableProperty]
    private string _sourceRestToken = string.Empty;

    [ObservableProperty]
    private string _sourceRestUser = string.Empty;

    [ObservableProperty]
    private string _sourceRestPassword = string.Empty;

    [ObservableProperty]
    private string _sourceRestHeaders = string.Empty;

    [ObservableProperty]
    private string _sourceRestPaging = "None";

    // ---- 入力（Salesforce 選択時: SOQL） ----

    [ObservableProperty]
    private string _sourceSoql = string.Empty;

    // ---- 出力（Database 選択時） ----

    [ObservableProperty]
    private string _targetDbProvider = DbProviderKind.Sqlite.ToString();

    [ObservableProperty]
    private string _targetDbConnectionString = string.Empty;

    [ObservableProperty]
    private string _targetDbTable = string.Empty;

    [ObservableProperty]
    private string _targetDbKeyField = string.Empty;

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
        "Database" => new DbTableSource(
            new DbConnectionSpec(ParseProvider(SourceDbProvider), SourceDbConnectionString),
            SourceDbQuery),
        "REST" => new RestSource(new RestSourceOptions
        {
            Url = SourceRestUrl.Trim(),
            AuthKind = SourceRestAuth,
            BearerToken = NullIfBlank(SourceRestToken),
            BasicUser = NullIfBlank(SourceRestUser),
            BasicPassword = NullIfBlank(SourceRestPassword),
            Headers = NullIfBlank(SourceRestHeaders),
            Paging = SourceRestPaging,
        }),
        "Salesforce" => new SalesforceSource(_rest, _targetOrg(), SourceSoql),
        _ => throw new InvalidOperationException("未対応の入力種別です: " + SelectedSourceType),
    };

    private static DbProviderKind ParseProvider(string name)
        => Enum.TryParse<DbProviderKind>(name, ignoreCase: true, out var kind) ? kind : DbProviderKind.Sqlite;

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

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

        if (SelectedTargetType == "Database")
        {
            var dbTarget = new DbTableTarget(
                new DbConnectionSpec(ParseProvider(TargetDbProvider), TargetDbConnectionString),
                new DbTableTargetOptions
                {
                    Table = TargetDbTable.Trim(),
                    Fields = fields,
                    Op = SelectedOp,
                    KeyField = string.IsNullOrWhiteSpace(TargetDbKeyField) ? null : TargetDbKeyField.Trim(),
                });
            return (dbTarget, dbTarget);
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
