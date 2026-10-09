using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
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

    public string SourceDbProvider { get; set; } = DbProviderKind.Sqlite.ToString();

    public string SourceDbConnectionString { get; set; } = string.Empty;

    public string SourceDbQuery { get; set; } = string.Empty;

    public string TargetDbProvider { get; set; } = DbProviderKind.Sqlite.ToString();

    public string TargetDbConnectionString { get; set; } = string.Empty;

    public string TargetDbTable { get; set; } = string.Empty;

    public string TargetDbKeyField { get; set; } = string.Empty;

    public string SourceRestUrl { get; set; } = string.Empty;

    public string SourceRestAuth { get; set; } = "None";

    public string SourceRestToken { get; set; } = string.Empty;

    public string SourceRestUser { get; set; } = string.Empty;

    public string SourceRestPassword { get; set; } = string.Empty;

    public string SourceRestHeaders { get; set; } = string.Empty;

    public string SourceRestPaging { get; set; } = "None";

    public string SourceSoql { get; set; } = string.Empty;

    public string DeltaColumn { get; set; } = string.Empty;

    public string DeltaStateKey { get; set; } = string.Empty;

    public bool SourceUseBulk { get; set; }

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
    private readonly SfCliRunner _sfCli;
    private readonly ICredentialProtector _protector;
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
        SfCliRunner sfCli,
        ICredentialProtector protector,
        BackupService backup)
    {
        _log = log;
        _paths = paths;
        _dialogs = dialogs;
        _files = files;
        _dispatcher = dispatcher;
        _rest = rest;
        _sfCli = sfCli;
        _protector = protector;
        _backup = backup;
    }

    public string Title => UiText.T("Etl_Title");

    public OrgInfo? Org { get; private set; }

    public string TargetOrg => Org is null ? string.Empty : (string.IsNullOrWhiteSpace(Org.Alias) ? Org.Username : Org.Alias!);

    public bool HasOrg => Org is not null;

    // ---- ジョブ エディタ（ステップ リスト + 選択ステップの詳細） ----

    /// <summary>ステップ（上から順に実行。親 → 子の順に並べる）。</summary>
    public ObservableCollection<EtlStepViewModel> Steps { get; } = new();

    [ObservableProperty]
    private EtlStepViewModel? _selectedStep;

    public bool HasSelectedStep => SelectedStep is not null;

    partial void OnSelectedStepChanged(EtlStepViewModel? value) => OnPropertyChanged(nameof(HasSelectedStep));

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
        RefreshSavedJobs();
        if (Steps.Count == 0)
        {
            AddStep();
        }
    }

    // ---- ステップ管理 ----

    private EtlStepViewModel CreateStepViewModel(string stepId)
        => new(stepId, _dialogs, _files, _paths, message => StatusMessage = message, _rest, _sfCli, () => TargetOrg);

    [RelayCommand]
    private void AddStep()
    {
        var index = 1;
        while (Steps.Any(s => s.StepId == "step" + index))
        {
            index++;
        }

        var step = CreateStepViewModel("step" + index);
        Steps.Add(step);
        SelectedStep = step;
    }

    [RelayCommand]
    private void RemoveStep()
    {
        if (SelectedStep is null || Steps.Count <= 1)
        {
            return;
        }

        var index = Steps.IndexOf(SelectedStep);
        Steps.Remove(SelectedStep);
        SelectedStep = Steps[Math.Min(index, Steps.Count - 1)];
    }

    [RelayCommand]
    private void MoveStepUp()
    {
        if (SelectedStep is null)
        {
            return;
        }

        var index = Steps.IndexOf(SelectedStep);
        if (index > 0)
        {
            Steps.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveStepDown()
    {
        if (SelectedStep is null)
        {
            return;
        }

        var index = Steps.IndexOf(SelectedStep);
        if (index >= 0 && index < Steps.Count - 1)
        {
            Steps.Move(index, index + 1);
        }
    }

    // ---- 実行 ----

    [RelayCommand]
    private async Task DryRunAsync() => await RunCoreAsync(dryRun: true);

    [RelayCommand]
    private async Task RunAsync() => await RunCoreAsync(dryRun: false);

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    /// <summary>前回実行の失敗行のみを再実行する（ステージング済みのキューを再利用し、ソースは再読込しない）。</summary>
    [RelayCommand]
    private async Task RetryFailedAsync()
    {
        if (IsRunning)
        {
            return;
        }

        if (_store is null)
        {
            StatusMessage = UiText.T("Etl_RetryNoRun");
            return;
        }

        var retryable = new List<(EtlStepViewModel Step, int Failed)>();
        foreach (var step in Steps)
        {
            var objectName = step.EffectiveObjectName;
            if (!_store.QueueTableExists(objectName))
            {
                continue;
            }

            var counts = _store.CountQueueByStatus(objectName);
            if (counts.TryGetValue(QueueStatus.Failed, out var failed) && failed > 0)
            {
                retryable.Add((step, failed));
            }
        }

        if (retryable.Count == 0)
        {
            StatusMessage = UiText.T("Etl_RetryNoFailed");
            return;
        }

        if (retryable.Any(r => r.Step.SelectedTargetType == "Salesforce") &&
            !_dialogs.Confirm(
                string.Join(", ", retryable.Select(r => r.Step.EffectiveObjectName)),
                UiText.T("Etl_RetryFailed")))
        {
            return;
        }

        IsRunning = true;
        CountsText = string.Empty;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var options = new EtlApplyOptions
            {
                BatchSize = Math.Max(1, BatchSize),
                MaxErrorRate = ParseErrorRate(),
            };

            foreach (var (step, failed) in retryable)
            {
                ct.ThrowIfCancellationRequested();
                AppendLog(UiText.T("Etl_RetryStartFmt", step.StepId, failed));
                _store.ResetFailedToPending(step.EffectiveObjectName);

                // crosswalk キーの位置を決める（マッピングのみから算出。ソースは読まない）
                var crosswalkIndex = -1;
                if (!string.IsNullOrWhiteSpace(step.CrosswalkKeyField))
                {
                    var columns = step.CreateMapper(new ExpressionEngine()).StagingColumns;
                    for (var i = 0; i < columns.Count; i++)
                    {
                        if (string.Equals(columns[i].Name, step.CrosswalkKeyField.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            crosswalkIndex = i;
                            break;
                        }
                    }
                }

                var (target, _) = step.CreateTarget(_rest, TargetOrg, _log);
                var runner = new EtlRunner(_store, target, step.StepId, step.EffectiveObjectName, options, crosswalkIndex);
                runner.Progress += p => _dispatcher.Post(() =>
                    CountsText = step.StepId + " " + UiText.T("Etl_ProgressFmt", p.Attempted, p.Success, p.Failed, p.Skipped));
                var result = await Task.Run(() => runner.RunAsync(dryRun: false, ct), ct);
                AppendLog(step.StepId + ": " + UiText.T("Etl_CompleteFmt", result.Success, result.Failed, result.Pending, result.StopReason));
            }

            StatusMessage = UiText.T("Etl_RetryFailed");
            RefreshRuns();
        }
        catch (OperationCanceledException)
        {
            AppendLog(UiText.T("Etl_Stop"));
            StatusMessage = UiText.T("Etl_Stop");
        }
        catch (Exception ex)
        {
            _log.Error("ETL 失敗行の再実行に失敗しました", ex);
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

    private async Task RunCoreAsync(bool dryRun)
    {
        if (IsRunning)
        {
            return;
        }

        if (Steps.Count == 0)
        {
            _dialogs.Warning(UiText.T("Etl_AddStep"), UiText.T("Etl_Title"));
            return;
        }

        foreach (var step in Steps)
        {
            if (step.Mappings.Count == 0)
            {
                SelectedStep = step;
                _dialogs.Warning(UiText.T("Etl_Load"), UiText.T("Etl_Title"));
                return;
            }

            if (step.SelectedSourceType == "REST" && string.IsNullOrWhiteSpace(step.SourceRestUrl))
            {
                SelectedStep = step;
                _dialogs.Warning(UiText.T("Etl_RestSettings"), UiText.T("Etl_Title"));
                return;
            }

            if (step.SelectedSourceType == "Salesforce" && (!HasOrg || string.IsNullOrWhiteSpace(step.SourceSoql)))
            {
                SelectedStep = step;
                _dialogs.Warning(UiText.T("Etl_SoqlSettings"), UiText.T("Etl_Title"));
                return;
            }

            if (!string.IsNullOrWhiteSpace(step.DeltaColumn) && string.IsNullOrWhiteSpace(step.DeltaStateKey))
            {
                SelectedStep = step;
                _dialogs.Warning(UiText.T("Etl_DeltaSettings"), UiText.T("Etl_Title"));
                return;
            }

            if (step.SelectedTargetType == "Salesforce")
            {
                if (!HasOrg)
                {
                    _dialogs.Warning(UiText.T("Etl_OrgRequired"), UiText.T("Etl_Title"));
                    return;
                }

                if (step.SelectedOp != RowOp.Insert && string.IsNullOrWhiteSpace(step.MatchKeyField))
                {
                    SelectedStep = step;
                    _dialogs.Warning(UiText.T("Etl_MatchKey"), UiText.T("Etl_Title"));
                    return;
                }
            }
            else if (step.SelectedTargetType == "Database")
            {
                if (string.IsNullOrWhiteSpace(step.TargetDbTable) || string.IsNullOrWhiteSpace(step.TargetDbConnectionString))
                {
                    SelectedStep = step;
                    _dialogs.Warning(UiText.T("Etl_DbSettings"), UiText.T("Etl_Title"));
                    return;
                }
            }
            else if (string.IsNullOrWhiteSpace(step.OutputPath))
            {
                SelectedStep = step;
                _dialogs.Warning(UiText.T("Etl_OutputPath"), UiText.T("Etl_Title"));
                return;
            }
        }

        var salesforceSteps = Steps.Where(s => s.SelectedTargetType == "Salesforce").ToList();
        if (!dryRun && salesforceSteps.Count > 0)
        {
            // 組織への書き込みは確認（CSV などローカル出力は即実行）
            var summary = string.Join(", ", salesforceSteps.Select(s => $"{s.EffectiveObjectName}/{s.SelectedOp}"));
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
            var runId = "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var runDirectory = Path.Combine(_paths.EtlRunsRoot, runId);
            _store?.Dispose();
            _store = RunStagingStore.Create(runDirectory, runId);

            // 式ホスト（LOOKUP はストア生成後に配線。複数ステップの親 → 子 Id 解決に使用）
            var host = new ExpressionHost
            {
                UserName = Environment.UserName,
                OrgName = TargetOrg,
                MachineName = Environment.MachineName,
            };
            EtlCrosswalk.WireLookup(host, _store);
            var engine = new ExpressionEngine(host);

            var plans = new List<EtlStepPlan>();
            var deltaStates = new Dictionary<string, (DeltaSource Source, string StatePath)>(StringComparer.Ordinal);
            foreach (var step in Steps)
            {
                var (target, revertable) = step.CreateTarget(_rest, TargetOrg, _log);
                var source = step.CreateSource();
                if (!string.IsNullOrWhiteSpace(step.DeltaColumn))
                {
                    // delta（差分）: 前回 watermark より新しい行のみを読み込む
                    var statePath = DeltaState.FilePath(_paths.EtlJobsRoot, step.DeltaStateKey.Trim());
                    var watermark = DeltaState.GetWatermark(statePath, step.StepId, _log);
                    var delta = new DeltaSource(source, step.DeltaColumn, watermark);
                    deltaStates[step.StepId] = (delta, statePath);
                    AppendLog(UiText.T("Etl_DeltaStartFmt", step.StepId, watermark?.ToString("u") ?? "-"));
                    source = delta;
                }

                plans.Add(new EtlStepPlan
                {
                    StepId = step.StepId,
                    ObjectName = step.EffectiveObjectName,
                    Source = source,
                    Mapper = step.CreateMapper(engine),
                    Target = target,
                    Revertable = revertable,
                    CrosswalkKeyField = string.IsNullOrWhiteSpace(step.CrosswalkKeyField) ? null : step.CrosswalkKeyField.Trim(),
                });
            }

            var options = new EtlApplyOptions
            {
                BatchSize = Math.Max(1, BatchSize),
                MaxErrorRate = ParseErrorRate(),
            };

            Func<CancellationToken, Task<string?>>? backupHook = null;
            if (!dryRun && RunBackupBefore && salesforceSteps.Count > 0)
            {
                var orgInfo = Org!;
                var backupObjects = salesforceSteps.Select(s => s.EffectiveObjectName).Distinct().ToList();
                backupHook = async backupCt =>
                {
                    var appVersion = typeof(EtlViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
                    var progress = new Progress<BackupProgress>(p => _dispatcher.Post(() =>
                        StatusMessage = UiText.T("Etl_BackupProgressFmt", p.Done, p.Total, p.ObjectName)));
                    var metadata = await _backup.RunBackupAsync(
                        TargetOrg, orgInfo.DisplayName, orgInfo.Username, orgInfo.OrgId, appVersion,
                        UiText.T("Etl_BackupLabel"), runId + " / " + string.Join(",", backupObjects),
                        backupObjects, progress, backupCt);
                    AppendLog(UiText.T("Etl_BackupDoneFmt", metadata.Id, metadata.TotalRecords));
                    return metadata.Id;
                };
            }

            if (plans.Count == 1)
            {
                var stepRun = new EtlStepRun(_store, plans[0], options)
                {
                    PreRunBackup = backupHook,
                };
                stepRun.Progress += p => _dispatcher.Post(() =>
                    CountsText = UiText.T("Etl_ProgressFmt", p.Attempted, p.Success, p.Failed, p.Skipped));

                var result = await Task.Run(() => stepRun.RunAsync(dryRun, autoRollbackOnFailure: true, ct), ct);

                var apply = result.Apply!;
                AppendLog(UiText.T("Etl_SourceLoadedFmt", result.Loaded));
                AppendLog(UiText.T("Etl_CompleteFmt", apply.Success, apply.Failed, apply.Pending, apply.StopReason));
                if (result.Rollback is not null)
                {
                    AppendLog(UiText.T("Etl_RevertedFmt", result.Rollback.Reverted, result.Rollback.Failed));
                }

                if (!dryRun)
                {
                    SaveDeltaWatermark(deltaStates, plans[0].StepId, apply);
                }
            }
            else
            {
                // マルチステップ（親 → 子、停止時は子 → 親の逆順ロールバック）
                var job = new EtlJobRunner(_store, new EtlJobPlan { JobName = "job:" + runId, Steps = plans }, options)
                {
                    PreRunBackup = backupHook,
                };
                job.StepProgress += (stepId, p) => _dispatcher.Post(() =>
                    CountsText = stepId + " " + UiText.T("Etl_ProgressFmt", p.Attempted, p.Success, p.Failed, p.Skipped));

                var jobResult = await Task.Run(() => job.RunAsync(dryRun, autoRollbackOnFailure: true, ct), ct);

                for (var i = 0; i < jobResult.Steps.Count && i < jobResult.StepIds.Count; i++)
                {
                    if (jobResult.Steps[i].Apply is { } stepApply)
                    {
                        AppendLog(jobResult.StepIds[i] + ": " + UiText.T("Etl_CompleteFmt", stepApply.Success, stepApply.Failed, stepApply.Pending, stepApply.StopReason));
                    }
                }

                if (!dryRun)
                {
                    for (var i = 0; i < jobResult.Steps.Count && i < jobResult.StepIds.Count; i++)
                    {
                        if (jobResult.Steps[i].Apply is { } stepApply)
                        {
                            SaveDeltaWatermark(deltaStates, jobResult.StepIds[i], stepApply);
                        }
                    }
                }

                foreach (var rollback in jobResult.Rollbacks)
                {
                    AppendLog(UiText.T("Etl_RevertedFmt", rollback.Reverted, rollback.Failed));
                }

                foreach (var error in jobResult.RollbackErrors)
                {
                    AppendLog(UiText.T("Common_FailedFmt", error));
                }
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

    private void AppendLog(string line)
        => _dispatcher.Post(() => LogLines.Add(DateTime.Now.ToString("HH:mm:ss") + " " + line));

    /// <summary>成功したステップの delta watermark を保存する（停止・失敗行ありは保存しない）。</summary>
    private void SaveDeltaWatermark(
        Dictionary<string, (DeltaSource Source, string StatePath)> deltaStates,
        string stepId,
        EtlRunResult apply)
    {
        if (apply.Stopped || apply.Failed != 0)
        {
            return;
        }

        if (!deltaStates.TryGetValue(stepId, out var delta))
        {
            return;
        }

        if (delta.Source.MaxValue is not { } max)
        {
            return;
        }

        DeltaState.SetWatermark(delta.StatePath, stepId, max, _log);
        AppendLog(UiText.T("Etl_DeltaSavedFmt", stepId, max.ToString("u")));
    }

    // ---- ジョブ保存 / 読込（data/etl/jobs/<name>.json） ----

    public ObservableCollection<string> SavedJobs { get; } = new();

    [ObservableProperty]
    private string _jobName = string.Empty;

    [ObservableProperty]
    private string? _selectedSavedJob;

    partial void OnSelectedSavedJobChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            JobName = value!;
        }
    }

    private void RefreshSavedJobs()
    {
        SavedJobs.Clear();
        foreach (var name in EtlJobStore.List(_paths))
        {
            SavedJobs.Add(name);
        }
    }

    [RelayCommand]
    private void SaveJob()
    {
        var name = JobName.Trim();
        if (name.Length == 0)
        {
            _dialogs.Warning(UiText.T("Etl_JobName"), Title);
            return;
        }

        if (File.Exists(EtlJobStore.FilePath(_paths, name)) &&
            !_dialogs.Confirm(UiText.T("Etl_JobOverwriteFmt", name), Title))
        {
            return;
        }

        // delta 列に状態キーが未設定ならジョブ名を既定にする（watermark がジョブ単位で安定する）
        foreach (var step in Steps)
        {
            if (!string.IsNullOrWhiteSpace(step.DeltaColumn) && string.IsNullOrWhiteSpace(step.DeltaStateKey))
            {
                step.DeltaStateKey = name;
            }
        }

        EtlJobStore.Save(_paths, BuildJobDefinition(name), _log);
        RefreshSavedJobs();
        SelectedSavedJob = name;
        StatusMessage = UiText.T("Etl_JobSavedFmt", name);
    }

    [RelayCommand]
    private void LoadJob()
    {
        var name = string.IsNullOrWhiteSpace(SelectedSavedJob) ? JobName.Trim() : SelectedSavedJob!.Trim();
        if (name.Length == 0)
        {
            _dialogs.Warning(UiText.T("Etl_JobName"), Title);
            return;
        }

        try
        {
            if (!File.Exists(EtlJobStore.FilePath(_paths, name)))
            {
                _dialogs.Warning(UiText.T("Common_FailedFmt", name), Title);
                return;
            }

            ApplyJobDefinition(EtlJobStore.Load(_paths, name, _log));
            JobName = name;
            StatusMessage = UiText.T("Etl_JobLoadedFmt", name);
        }
        catch (Exception ex)
        {
            _dialogs.Warning(UiText.T("Common_FailedFmt", ex.Message), Title);
        }
    }

    [RelayCommand]
    private void DeleteJob()
    {
        var name = string.IsNullOrWhiteSpace(SelectedSavedJob) ? JobName.Trim() : SelectedSavedJob!.Trim();
        if (name.Length == 0)
        {
            return;
        }

        if (!_dialogs.Confirm(UiText.T("Etl_JobDeleteConfirmFmt", name), Title))
        {
            return;
        }

        if (EtlJobStore.Delete(_paths, name))
        {
            RefreshSavedJobs();
            SelectedSavedJob = null;
            StatusMessage = UiText.T("Etl_JobDeletedFmt", name);
            _log.Info($"ETL ジョブ削除: {name}");
        }
    }

    private EtlJobDefinition BuildJobDefinition(string name)
    {
        var job = new EtlJobDefinition
        {
            Name = name,
            ErrorRateText = ErrorRateText,
            BatchSize = BatchSize,
            RunBackupBefore = RunBackupBefore,
        };

        foreach (var step in Steps)
        {
            job.Steps.Add(new EtlStepDefinition
            {
                StepId = step.StepId,
                SelectedSourceType = step.SelectedSourceType,
                SourcePath = step.SourcePath,
                SourceHasHeader = step.SourceHasHeader,
                ExcelSheet = step.ExcelSheet,
                XmlRowElement = step.XmlRowElement,
                SourceDbProvider = step.SourceDbProvider,
                SourceDbConnectionString = step.SourceDbConnectionString,
                SourceDbQuery = step.SourceDbQuery,
                SourceRestUrl = step.SourceRestUrl,
                SourceRestAuth = step.SourceRestAuth,
                SourceRestToken = step.SourceRestToken,
                SourceRestUser = step.SourceRestUser,
                SourceRestPassword = step.SourceRestPassword,
                SourceRestHeaders = step.SourceRestHeaders,
                SourceRestPaging = step.SourceRestPaging,
                SourceSoql = step.SourceSoql,
                SourceUseBulk = step.SourceUseBulk,
                DeltaColumn = step.DeltaColumn,
                DeltaStateKey = step.DeltaStateKey,
                SelectedTargetType = step.SelectedTargetType,
                ObjectApiName = step.ObjectApiName,
                SelectedOp = step.SelectedOp,
                MatchKeyField = step.MatchKeyField,
                OutputPath = step.OutputPath,
                CrosswalkKeyField = step.CrosswalkKeyField,
                TargetDbProvider = step.TargetDbProvider,
                TargetDbConnectionString = step.TargetDbConnectionString,
                TargetDbTable = step.TargetDbTable,
                TargetDbKeyField = step.TargetDbKeyField,
                Mappings = step.Mappings.Select(m => new EtlMappingDefinition
                {
                    SourceColumn = m.SourceColumn,
                    TargetField = m.TargetField,
                    Expression = m.Expression,
                    Type = m.Type,
                }).ToList(),
            });
        }

        return job;
    }

    private void ApplyJobDefinition(EtlJobDefinition job)
    {
        Steps.Clear();
        var index = 0;
        foreach (var def in job.Steps)
        {
            index++;
            var step = CreateStepViewModel(string.IsNullOrWhiteSpace(def.StepId) ? "step" + index : def.StepId);
            step.SelectedSourceType = def.SelectedSourceType;
            step.SourcePath = def.SourcePath;
            step.SourceHasHeader = def.SourceHasHeader;
            step.ExcelSheet = def.ExcelSheet;
            step.XmlRowElement = def.XmlRowElement;
            step.SourceDbProvider = def.SourceDbProvider;
            step.SourceDbConnectionString = def.SourceDbConnectionString;
            step.SourceDbQuery = def.SourceDbQuery;
            step.SourceRestUrl = def.SourceRestUrl;
            step.SourceRestAuth = def.SourceRestAuth;
            step.SourceRestToken = def.SourceRestToken;
            step.SourceRestUser = def.SourceRestUser;
            step.SourceRestPassword = def.SourceRestPassword;
            step.SourceRestHeaders = def.SourceRestHeaders;
            step.SourceRestPaging = def.SourceRestPaging;
            step.SourceSoql = def.SourceSoql;
            step.SourceUseBulk = def.SourceUseBulk;
            step.DeltaColumn = def.DeltaColumn;
            step.DeltaStateKey = def.DeltaStateKey;
            step.SelectedTargetType = def.SelectedTargetType;
            step.ObjectApiName = def.ObjectApiName;
            step.SelectedOp = def.SelectedOp;
            step.MatchKeyField = def.MatchKeyField;
            step.OutputPath = def.OutputPath;
            step.CrosswalkKeyField = def.CrosswalkKeyField;
            step.TargetDbProvider = def.TargetDbProvider;
            step.TargetDbConnectionString = def.TargetDbConnectionString;
            step.TargetDbTable = def.TargetDbTable;
            step.TargetDbKeyField = def.TargetDbKeyField;
            foreach (var mapping in def.Mappings)
            {
                step.Mappings.Add(new EtlMappingRow(mapping.SourceColumn)
                {
                    TargetField = string.IsNullOrEmpty(mapping.TargetField) ? mapping.SourceColumn : mapping.TargetField,
                    Expression = mapping.Expression,
                    Type = mapping.Type,
                });
            }

            Steps.Add(step);
        }

        if (Steps.Count == 0)
        {
            AddStep();
        }
        else
        {
            SelectedStep = Steps[0];
        }

        ErrorRateText = job.ErrorRateText;
        BatchSize = job.BatchSize;
        RunBackupBefore = job.RunBackupBefore;
    }

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
        if (SelectedStep is null)
        {
            return;
        }

        var step = SelectedStep;
        var name = _dialogs.Prompt(UiText.T("Etl_SaveConnection"), UiText.T("Etl_SaveConnection"), TargetOrg);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Connections.Add(new EtlConnection
        {
            Name = name.Trim(),
            SourceType = step.SelectedSourceType,
            SourcePath = step.SourcePath,
            TargetType = step.SelectedTargetType,
            ObjectApiName = step.ObjectApiName,
            Op = step.SelectedOp,
            MatchKeyField = step.MatchKeyField,
            OutputPath = step.OutputPath,
            SourceDbProvider = step.SourceDbProvider,
            SourceDbConnectionString = step.SourceDbConnectionString,
            SourceDbQuery = step.SourceDbQuery,
            TargetDbProvider = step.TargetDbProvider,
            TargetDbConnectionString = step.TargetDbConnectionString,
            TargetDbTable = step.TargetDbTable,
            TargetDbKeyField = step.TargetDbKeyField,
            SourceRestUrl = step.SourceRestUrl,
            SourceRestAuth = step.SourceRestAuth,
            SourceRestToken = step.SourceRestToken,
            SourceRestUser = step.SourceRestUser,
            SourceRestPassword = step.SourceRestPassword,
            SourceRestHeaders = step.SourceRestHeaders,
            SourceRestPaging = step.SourceRestPaging,
            SourceSoql = step.SourceSoql,
            DeltaColumn = step.DeltaColumn,
            DeltaStateKey = step.DeltaStateKey,
            SourceUseBulk = step.SourceUseBulk,
        });
        SaveConnectionsFile();
    }

    [RelayCommand]
    private void UseConnection()
    {
        if (SelectedConnection is null || SelectedStep is null)
        {
            return;
        }

        var step = SelectedStep;
        step.SelectedSourceType = SelectedConnection.SourceType;
        step.SourcePath = SelectedConnection.SourcePath;
        step.SelectedTargetType = SelectedConnection.TargetType;
        step.ObjectApiName = SelectedConnection.ObjectApiName;
        step.SelectedOp = SelectedConnection.Op;
        step.MatchKeyField = SelectedConnection.MatchKeyField;
        step.OutputPath = SelectedConnection.OutputPath;
        step.SourceDbProvider = SelectedConnection.SourceDbProvider;
        step.SourceDbConnectionString = SelectedConnection.SourceDbConnectionString;
        step.SourceDbQuery = SelectedConnection.SourceDbQuery;
        step.TargetDbProvider = SelectedConnection.TargetDbProvider;
        step.TargetDbConnectionString = SelectedConnection.TargetDbConnectionString;
        step.TargetDbTable = SelectedConnection.TargetDbTable;
        step.TargetDbKeyField = SelectedConnection.TargetDbKeyField;
        step.SourceRestUrl = SelectedConnection.SourceRestUrl;
        step.SourceRestAuth = SelectedConnection.SourceRestAuth;
        step.SourceRestToken = SelectedConnection.SourceRestToken;
        step.SourceRestUser = SelectedConnection.SourceRestUser;
        step.SourceRestPassword = SelectedConnection.SourceRestPassword;
        step.SourceRestHeaders = SelectedConnection.SourceRestHeaders;
        step.SourceRestPaging = SelectedConnection.SourceRestPaging;
        step.SourceSoql = SelectedConnection.SourceSoql;
        step.DeltaColumn = SelectedConnection.DeltaColumn;
        step.DeltaStateKey = SelectedConnection.DeltaStateKey;
        step.SourceUseBulk = SelectedConnection.SourceUseBulk;
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
            var list = items ?? new List<EtlConnection>();
            EtlConnectionSecrets.UnprotectInPlace(list, _protector);   // 保存時に保護された値を平文へ戻す
            foreach (var item in list)
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

            // 秘密情報はファイルへ書く間だけ保護する（UI 上は平文のまま）
            var list = Connections.ToList();
            EtlConnectionSecrets.ProtectInPlace(list, _protector);
            try
            {
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConnectionsFile, json, new UTF8Encoding(false));
            }
            finally
            {
                EtlConnectionSecrets.UnprotectInPlace(list, _protector);
            }
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
