using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
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

/// <summary>マッピング グリッドの 1 行（ソース列 → ターゲット項目 + 型 + 式）。</summary>
public sealed partial class EtlMappingRow : ObservableObject
{
    public EtlMappingRow(string sourceColumn)
    {
        SourceColumn = sourceColumn;
        _targetField = sourceColumn;
        _expression = "[" + sourceColumn + "]";
    }

    /// <summary>ソース列名（読み取り専用）。</summary>
    public string SourceColumn { get; }

    [ObservableProperty]
    private string _targetField;

    [ObservableProperty]
    private string _expression;

    [ObservableProperty]
    private StagingColumnType _type = StagingColumnType.Text;

    public IReadOnlyList<StagingColumnType> TypeOptions { get; } = new[]
    {
        StagingColumnType.Text,
        StagingColumnType.Integer,
        StagingColumnType.Real,
        StagingColumnType.Boolean,
        StagingColumnType.DateTime,
    };
}

/// <summary>復元マネージャー用: 実行 1 件。</summary>
public sealed record EtlRunItem(string RunId, string Directory);

/// <summary>復元マネージャー用: journal 1 行。</summary>
public sealed class EtlJournalItem
{
    public EtlJournalItem(JournalRow row)
    {
        Row = row;
    }

    public JournalRow Row { get; }

    public string StepId => Row.StepId;

    public string ObjectName => Row.ObjectName;

    public string Op => Row.Op;

    public string TargetId => Row.TargetId ?? string.Empty;

    public string Status => Row.RevertStatus ?? string.Empty;

    public string AppliedAt => Row.AppliedAt;
}

/// <summary>接続マネージャーの保存済み接続。</summary>
public sealed class EtlConnection
{
    public string Name { get; set; } = string.Empty;

    public string SourceType { get; set; } = "CSV";

    public string SourcePath { get; set; } = string.Empty;

    public string TargetType { get; set; } = "Salesforce";

    public string ObjectApiName { get; set; } = string.Empty;

    public string Op { get; set; } = RowOp.Insert;

    public string MatchKeyField { get; set; } = string.Empty;

    public string OutputPath { get; set; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>
/// ETL ウィンドウの ViewModel（4 領域: ジョブ エディタ / 実行モニター / 復元マネージャー / 接続マネージャー）。
/// 実行は SfUi.Etl の EtlStepRun（Prepare → Apply → 自動ロールバック）をそのまま使う。
/// </summary>
public sealed partial class EtlViewModel : ObservableObject, IDisposable
{
    private readonly AppLog _log;
    private readonly AppPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _files;
    private readonly IUiDispatcher _dispatcher;
    private readonly SalesforceRestClient _rest;
    private readonly BackupService _backup;
    private CancellationTokenSource? _cts;
    private RunStagingStore? _store;

    public EtlViewModel(
        AppLog log,
        AppPaths paths,
        IDialogService dialogs,
        IFilePickerService files,
        IUiDispatcher dispatcher,
        SalesforceRestClient rest,
        BackupService backup)
    {
        _log = log;
        _paths = paths;
        _dialogs = dialogs;
        _files = files;
        _dispatcher = dispatcher;
        _rest = rest;
        _backup = backup;
    }

    public string Title => UiText.T("Etl_Title");

    public OrgInfo? Org { get; private set; }

    public string TargetOrg => Org is null ? string.Empty : (string.IsNullOrWhiteSpace(Org.Alias) ? Org.Username : Org.Alias!);

    public bool HasOrg => Org is not null;

    // ---- ジョブ エディタ ----

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

    [ObservableProperty]
    private string _errorRateText = "5";

    [ObservableProperty]
    private int _batchSize = 200;

    /// <summary>Salesforce 出力の実行前に、対象オブジェクトをバックアップするか（既定: 有効）。</summary>
    [ObservableProperty]
    private bool _runBackupBefore = true;

    // ---- 実行モニター ----

    [ObservableProperty]
    private bool _isRunning;

    public bool IsNotRunning => !IsRunning;

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(IsNotRunning));

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _countsText = string.Empty;

    public ObservableCollection<string> LogLines { get; } = new();

    // ---- 復元マネージャー ----

    public ObservableCollection<EtlRunItem> Runs { get; } = new();

    [ObservableProperty]
    private EtlRunItem? _selectedRun;

    public ObservableCollection<EtlJournalItem> JournalItems { get; } = new();

    [ObservableProperty]
    private EtlJournalItem? _selectedJournal;

    // ---- 接続マネージャー ----

    public ObservableCollection<EtlConnection> Connections { get; } = new();

    [ObservableProperty]
    private EtlConnection? _selectedConnection;

    private string ConnectionsFile => Path.Combine(_paths.DataRoot, "etl", "connections.json");

    public void Initialize(OrgInfo? org)
    {
        Org = org;
        OnPropertyChanged(nameof(TargetOrg));
        OnPropertyChanged(nameof(HasOrg));
        StatusMessage = UiText.T("Etl_MonitorIdle");
        RefreshRuns();
        LoadConnections();
    }

    // ---- ジョブ エディタ コマンド ----

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
        var path = await _files.SaveFileAsync(UiText.T("Etl_OutputPath"), ObjectApiName + ".csv", UiText.T("Etl_OutputFilter"), _paths.DataRoot);
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

            StatusMessage = UiText.T("Etl_SourceLoadedFmt", Mappings.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warn($"ETL ソースの読み込みに失敗しました: {ex.Message}");
            _dialogs.Warning(ex.Message, UiText.T("Etl_Title"));
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
        }
    }

    // ---- 実行 ----

    [RelayCommand]
    private async Task DryRunAsync() => await RunCoreAsync(dryRun: true);

    [RelayCommand]
    private async Task RunAsync() => await RunCoreAsync(dryRun: false);

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    private async Task RunCoreAsync(bool dryRun)
    {
        if (IsRunning)
        {
            return;
        }

        if (Mappings.Count == 0)
        {
            _dialogs.Warning(UiText.T("Etl_Load"), UiText.T("Etl_Title"));
            return;
        }

        if (SelectedTargetType == "Salesforce")
        {
            if (!HasOrg)
            {
                _dialogs.Warning(UiText.T("Etl_OrgRequired"), UiText.T("Etl_Title"));
                return;
            }

            if (SelectedOp != RowOp.Insert && string.IsNullOrWhiteSpace(MatchKeyField))
            {
                _dialogs.Warning(UiText.T("Etl_MatchKey"), UiText.T("Etl_Title"));
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(OutputPath))
        {
            _dialogs.Warning(UiText.T("Etl_OutputPath"), UiText.T("Etl_Title"));
            return;
        }

        if (!dryRun && SelectedTargetType == "Salesforce")
        {
            // 組織への書き込みは確認（CSV などローカル出力は即実行）
            var summary = $"{SelectedTargetType}: {ObjectApiName} / {SelectedOp}";
            if (!_dialogs.Confirm(summary, UiText.T("Etl_Run")))
            {
                return;
            }
        }

        IsRunning = true;
        CountsText = string.Empty;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var mapper = BuildMapper();
            var source = CreateSource();
            var (target, revertable) = CreateTarget();

            var runId = "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var runDirectory = Path.Combine(_paths.EtlRunsRoot, runId);
            _store?.Dispose();
            _store = RunStagingStore.Create(runDirectory, runId);

            var plan = new EtlStepPlan
            {
                StepId = "step1",
                ObjectName = string.IsNullOrWhiteSpace(ObjectApiName) ? "Output" : ObjectApiName.Trim(),
                Source = source,
                Mapper = mapper,
                Target = target,
                Revertable = revertable,
            };

            var options = new EtlApplyOptions
            {
                BatchSize = Math.Max(1, BatchSize),
                MaxErrorRate = ParseErrorRate(),
            };

            var stepRun = new EtlStepRun(_store, plan, options);
            stepRun.Progress += p => _dispatcher.Post(() =>
                CountsText = UiText.T("Etl_ProgressFmt", p.Attempted, p.Success, p.Failed, p.Skipped));

            if (!dryRun && SelectedTargetType == "Salesforce" && RunBackupBefore)
            {
                var orgInfo = Org!;
                var objectName = plan.ObjectName;
                stepRun.PreRunBackup = async backupCt =>
                {
                    var appVersion = typeof(EtlViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
                    var progress = new Progress<BackupProgress>(p => _dispatcher.Post(() =>
                        StatusMessage = UiText.T("Etl_BackupProgressFmt", p.Done, p.Total, p.ObjectName)));
                    var metadata = await _backup.RunBackupAsync(
                        TargetOrg, orgInfo.DisplayName, orgInfo.Username, orgInfo.OrgId, appVersion,
                        UiText.T("Etl_BackupLabel"), runId + " / " + objectName,
                        new[] { objectName }, progress, backupCt);
                    AppendLog(UiText.T("Etl_BackupDoneFmt", metadata.Id, metadata.TotalRecords));
                    return metadata.Id;
                };
            }

            var result = await Task.Run(() => stepRun.RunAsync(dryRun, autoRollbackOnFailure: true, ct), ct);

            var apply = result.Apply!;
            AppendLog(UiText.T("Etl_SourceLoadedFmt", result.Loaded));
            AppendLog(UiText.T("Etl_CompleteFmt", apply.Success, apply.Failed, apply.Pending, apply.StopReason));
            if (result.Rollback is not null)
            {
                AppendLog(UiText.T("Etl_RevertedFmt", result.Rollback.Reverted, result.Rollback.Failed));
            }

            StatusMessage = dryRun ? UiText.T("Etl_DryRun") : UiText.T("Etl_Run");
            RefreshRuns();
        }
        catch (OperationCanceledException)
        {
            AppendLog(UiText.T("Etl_Stop"));
            StatusMessage = UiText.T("Etl_Stop");
        }
        catch (Exception ex)
        {
            _log.Error("ETL の実行に失敗しました", ex);
            AppendLog(UiText.T("Common_FailedFmt", ex.Message));
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private double ParseErrorRate()
    {
        if (double.TryParse(ErrorRateText, out var percent) && percent > 0)
        {
            return Math.Min(1.0, percent / 100.0);
        }

        return 0.05;
    }

    private IEtlSource CreateSource() => SelectedSourceType switch
    {
        "CSV" => new CsvFileSource(SourcePath, new CsvStreamOptions { HasHeader = SourceHasHeader }),
        "TSV" => new CsvFileSource(SourcePath, new CsvStreamOptions { Delimiter = '\t', HasHeader = SourceHasHeader }),
        "Excel" => new ExcelFileSource(SourcePath, string.IsNullOrWhiteSpace(ExcelSheet) ? null : ExcelSheet, SourceHasHeader),
        "JSON" => new JsonFileSource(SourcePath),
        "XML" => new XmlFileSource(SourcePath, string.IsNullOrWhiteSpace(XmlRowElement) ? null : XmlRowElement),
        _ => throw new InvalidOperationException("未対応の入力種別です: " + SelectedSourceType),
    };

    private RowMapper BuildMapper()
    {
        var host = new ExpressionHost
        {
            UserName = Environment.UserName,
            OrgName = TargetOrg,
            MachineName = Environment.MachineName,
        };
        var engine = new ExpressionEngine(host);
        var mappings = Mappings
            .Select(m => new FieldMapping(m.TargetField, m.Expression, m.Type))
            .ToList();
        return new RowMapper(Mappings.Select(m => m.SourceColumn).ToList(), mappings, engine);
    }

    private (IEtlTarget Target, IEtlRevertable? Revertable) CreateTarget()
    {
        var fields = Mappings.Select(m => m.TargetField).ToList();
        if (SelectedTargetType == "CSV")
        {
            return (new CsvFileTarget(OutputPath, fields), null);
        }

        var target = new SalesforceTarget(_rest, TargetOrg, new SalesforceTargetOptions
        {
            ObjectName = ObjectApiName.Trim(),
            Fields = fields,
            Op = SelectedOp,
            MatchKeyField = string.IsNullOrWhiteSpace(MatchKeyField) ? null : MatchKeyField.Trim(),
        }, _log);
        return (target, target);
    }

    private void AppendLog(string line)
        => _dispatcher.Post(() => LogLines.Add(DateTime.Now.ToString("HH:mm:ss") + " " + line));

    // ---- 復元マネージャー ----

    [RelayCommand]
    private void RefreshRuns()
    {
        Runs.Clear();
        if (Directory.Exists(_paths.EtlRunsRoot))
        {
            foreach (var directory in Directory.GetDirectories(_paths.EtlRunsRoot).OrderByDescending(d => d))
            {
                Runs.Add(new EtlRunItem(Path.GetFileName(directory), directory));
            }
        }
    }

    [RelayCommand]
    private void LoadJournal()
    {
        JournalItems.Clear();
        if (SelectedRun is null)
        {
            StatusMessage = UiText.T("Etl_NoJournal");
            return;
        }

        try
        {
            using var store = RunStagingStore.Open(SelectedRun.Directory);
            foreach (var row in store.QueryJournal().OrderByDescending(r => r.Id))
            {
                JournalItems.Add(new EtlJournalItem(row));
            }

            if (JournalItems.Count == 0)
            {
                StatusMessage = UiText.T("Etl_NoJournal");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"journal の読み込みに失敗しました: {ex.Message}");
            _dialogs.Warning(ex.Message, UiText.T("Etl_Title"));
        }
    }

    [RelayCommand]
    private async Task RevertGroupAsync()
    {
        if (!HasOrg)
        {
            _dialogs.Warning(UiText.T("Etl_OrgRequired"), UiText.T("Etl_Title"));
            return;
        }

        if (SelectedRun is null || SelectedJournal is null)
        {
            return;
        }

        await RevertCoreAsync(SelectedRun, new[] { (SelectedJournal.StepId, SelectedJournal.ObjectName) });
    }

    [RelayCommand]
    private async Task RevertAllAsync()
    {
        if (!HasOrg)
        {
            _dialogs.Warning(UiText.T("Etl_OrgRequired"), UiText.T("Etl_Title"));
            return;
        }

        if (SelectedRun is null || JournalItems.Count == 0)
        {
            return;
        }

        var groups = JournalItems
            .Select(j => (j.StepId, j.ObjectName))
            .Distinct()
            .ToList();
        await RevertCoreAsync(SelectedRun, groups);
    }

    private async Task RevertCoreAsync(EtlRunItem run, IReadOnlyList<(string StepId, string ObjectName)> groups)
    {
        if (!_dialogs.ConfirmDestructive(UiText.T("Etl_RevertAll"), UiText.T("Etl_Title")))
        {
            return;
        }

        IsRunning = true;
        var reverted = 0;
        var failed = 0;

        try
        {
            await Task.Run(async () =>
            {
                using var store = RunStagingStore.Open(run.Directory);
                foreach (var (stepId, objectName) in groups)
                {
                    var target = new SalesforceTarget(_rest, TargetOrg, new SalesforceTargetOptions
                    {
                        ObjectName = objectName,
                        Fields = Array.Empty<string>(),
                    }, _log);
                    var reverter = new JournalReverter(store, target, stepId, objectName);
                    var result = await reverter.RevertAsync();
                    reverted += result.Reverted;
                    failed += result.Failed;
                }
            });

            AppendLog(UiText.T("Etl_RevertedFmt", reverted, failed));
            LoadJournal();
        }
        catch (Exception ex)
        {
            _log.Error("巻き戻しに失敗しました", ex);
            _dialogs.Warning(ex.Message, UiText.T("Etl_Title"));
        }
        finally
        {
            IsRunning = false;
        }
    }

    // ---- 接続マネージャー ----

    [RelayCommand]
    private void SaveConnection()
    {
        var name = _dialogs.Prompt(UiText.T("Etl_SaveConnection"), UiText.T("Etl_SaveConnection"), TargetOrg);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Connections.Add(new EtlConnection
        {
            Name = name.Trim(),
            SourceType = SelectedSourceType,
            SourcePath = SourcePath,
            TargetType = SelectedTargetType,
            ObjectApiName = ObjectApiName,
            Op = SelectedOp,
            MatchKeyField = MatchKeyField,
            OutputPath = OutputPath,
        });
        SaveConnectionsFile();
    }

    [RelayCommand]
    private void UseConnection()
    {
        if (SelectedConnection is null)
        {
            return;
        }

        SelectedSourceType = SelectedConnection.SourceType;
        SourcePath = SelectedConnection.SourcePath;
        SelectedTargetType = SelectedConnection.TargetType;
        ObjectApiName = SelectedConnection.ObjectApiName;
        SelectedOp = SelectedConnection.Op;
        MatchKeyField = SelectedConnection.MatchKeyField;
        OutputPath = SelectedConnection.OutputPath;
    }

    [RelayCommand]
    private void DeleteConnection()
    {
        if (SelectedConnection is null)
        {
            return;
        }

        Connections.Remove(SelectedConnection);
        SaveConnectionsFile();
    }

    private void LoadConnections()
    {
        try
        {
            if (!File.Exists(ConnectionsFile))
            {
                return;
            }

            var items = JsonSerializer.Deserialize<List<EtlConnection>>(File.ReadAllText(ConnectionsFile));
            foreach (var item in items ?? new List<EtlConnection>())
            {
                Connections.Add(item);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"接続設定の読み込みに失敗しました: {ex.Message}");
        }
    }

    private void SaveConnectionsFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(ConnectionsFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(Connections.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConnectionsFile, json, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            _log.Warn($"接続設定の保存に失敗しました: {ex.Message}");
            _dialogs.Warning(ex.Message, UiText.T("Etl_Title"));
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _store?.Dispose();
    }
}
