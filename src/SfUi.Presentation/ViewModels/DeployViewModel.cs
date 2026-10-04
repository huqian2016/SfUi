using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>デプロイ / メタデータ操作タブの ViewModel。</summary>
public partial class DeployViewModel : ObservableObject
{
    private static readonly TimeSpan ExecuteTimeout = TimeSpan.FromMinutes(60);

    private readonly DeployService _service;
    private readonly HistoryStore _history;
    private readonly FavoritesStore _favorites;
    private readonly AppSettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _filePicker;
    private readonly IUiDispatcher _ui;
    private readonly AppLog _log;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private DeployOperationOption _operation = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private string? _sourceDir;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private string? _manifest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private string _testLevel = "NoTestRun";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private string? _tests;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private int _waitMinutes = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private string? _jobId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private bool _useMostRecent;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = UiText.T("Common_Ready");

    [ObservableProperty]
    private string? _outputText;

    [ObservableProperty]
    private HistoryEntry? _selectedHistoryItem;

    /// <summary>現在の対象組織（MainViewModel から設定される）。</summary>
    public string? CurrentOrg { get; set; }

    /// <summary>現在の SF 実行フォルダ（MainViewModel から設定される）。</summary>
    public string? CurrentFolder { get; set; }

    /// <summary>デプロイ操作の選択肢（コード + 表示ラベル）。</summary>
    public sealed record DeployOperationOption(string Code, string Label);

    /// <summary>操作コンボの選択肢（言語切替で再構築される）。</summary>
    public ObservableCollection<DeployOperationOption> Operations { get; } = new();

    public ObservableCollection<string> TestLevels { get; } = new()
    {
        "NoTestRun",
        "RunLocalTests",
        "RunAllTestsInOrg",
        "RunSpecifiedTests",
    };

    public ObservableCollection<HistoryEntry> HistoryItems { get; } = new();

    /// <summary>実行されるコマンドのプレビュー（sf から始まる表示用文字列）。</summary>
    public string CommandPreview => DeployService.ToDisplayCommand(DeployService.BuildArgs(BuildRequest()));

    public DeployViewModel(
        DeployService service, HistoryStore history, FavoritesStore favorites, AppSettingsStore settings,
        IDialogService dialogs, IFilePickerService filePicker, IUiDispatcher ui, AppLog log)
    {
        _service = service;
        _history = history;
        _favorites = favorites;
        _settings = settings;
        _dialogs = dialogs;
        _filePicker = filePicker;
        _ui = ui;
        _log = log;
        UiText.LanguageChanged += OnLanguageChanged;
        BuildOperations();
        RefreshHistory();
    }

    private void OnLanguageChanged()
    {
        if (_ui.CheckAccess())
        {
            BuildOperations(Operation.Code);
        }
        else
        {
            _ui.Invoke(() => BuildOperations(Operation.Code));
        }
    }

    /// <summary>操作コンボを現在の言語で再構築する（選択はコードで維持）。</summary>
    private void BuildOperations(string? selectedCode = null)
    {
        Operations.Clear();
        Operations.Add(new DeployOperationOption(DeployOperations.Deploy, UiText.T("Op_Deploy")));
        Operations.Add(new DeployOperationOption(DeployOperations.Validate, UiText.T("Op_Validate")));
        Operations.Add(new DeployOperationOption(DeployOperations.Quick, UiText.T("Op_Quick")));
        Operations.Add(new DeployOperationOption(DeployOperations.Report, UiText.T("Op_Report")));
        Operations.Add(new DeployOperationOption(DeployOperations.Retrieve, UiText.T("Op_Retrieve")));

        Operation = Operations.FirstOrDefault(o => o.Code == selectedCode) ?? Operations[0];
    }

    [RelayCommand]
    private void RefreshHistory()
    {
        HistoryItems.Clear();
        foreach (var entry in _history.Query(new HistoryQuery(Type: HistoryTypes.Deploy)).Take(30))
        {
            HistoryItems.Add(entry);
        }
    }

    partial void OnSelectedHistoryItemChanged(HistoryEntry? value)
    {
        if (value is not null)
        {
            LoadFromHistory(value, autoRun: false);
        }
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (IsRunning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentOrg))
        {
            StatusText = UiText.T("Msg_SelectOrg");
            return;
        }

        var args = DeployService.BuildArgs(BuildRequest());
        var command = DeployService.ToDisplayCommand(args);

        if (ConfirmPolicies.ShouldConfirm(_settings.Current.ConfirmPolicy, isDangerous: true))
        {
            var answer = _dialogs.Confirm(
                UiText.T("Deploy_ConfirmFmt", Environment.NewLine, command),
                "SfUi");
            if (!answer)
            {
                StatusText = UiText.T("Common_Canceled");
                return;
            }
        }

        IsRunning = true;
        StatusText = UiText.T("Deploy_Running");
        OutputText = null;
        _cts = new CancellationTokenSource();
        try
        {
            var raw = await _service.ExecuteAsync(args, CurrentFolder, ExecuteTimeout, _cts.Token);
            OutputText = JsonPretty.Prettify(raw.StdOut);
            var summary = TrySummarize(raw.StdOut);
            StatusText = UiText.T("Deploy_ExitFmt", raw.ExitCode, raw.Duration.TotalMinutes) + (summary is null ? string.Empty : $" / {summary}");
            AppendHistory(command, raw.Success ? "success" : "error", (int)raw.Duration.TotalMilliseconds, BuildResultText(raw));
            _log.Info($"デプロイ操作: {command} → 終了コード {raw.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            StatusText = UiText.T("Common_Canceled");
            AppendHistory(command, "canceled", 0, null);
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Common_FailedFmt", ex.Message);
            OutputText = ex.Message;
            AppendHistory(command, "error", 0, ex.Message);
            _log.Error("デプロイ操作に失敗", ex);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            RefreshHistory();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void AddFavorite()
    {
        var command = CommandPreview;
        _favorites.Add(HistoryTypes.Deploy, Summarize(command), command);
        StatusText = UiText.T("Common_FavoriteAddedFmt", Summarize(command));
    }

    [RelayCommand]
    private async Task BrowseSourceDirAsync()
    {
        var initialDirectory = !string.IsNullOrWhiteSpace(CurrentFolder) && Directory.Exists(CurrentFolder) ? CurrentFolder : null;
        var folder = await _filePicker.PickFolderAsync(UiText.T("Deploy_BrowseSourceTitle"), initialDirectory);
        if (folder is not null)
        {
            SourceDir = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseManifestAsync()
    {
        var initialDirectory = !string.IsNullOrWhiteSpace(CurrentFolder) && Directory.Exists(CurrentFolder) ? CurrentFolder : null;
        var fileName = await _filePicker.OpenFileAsync(UiText.T("Deploy_BrowseManifestTitle"), UiText.T("Deploy_ManifestFilter"), initialDirectory);
        if (fileName is not null)
        {
            Manifest = fileName;
        }
    }

    /// <summary>履歴から読み込む（autoRun=true ならそのまま実行）。</summary>
    public void LoadFromHistory(HistoryEntry entry, bool autoRun)
    {
        if (entry.Params is { Length: > 0 } text)
        {
            ApplyCommandLine(text);
        }

        if (autoRun)
        {
            _ = ExecuteAsync();
        }
    }

    /// <summary>お気に入りから読み込む（autoRun=true ならそのまま実行）。</summary>
    public void LoadFavorite(FavoriteItem item, bool autoRun)
    {
        if (item.Payload is { Length: > 0 } text)
        {
            ApplyCommandLine(text);
        }

        if (autoRun)
        {
            _ = ExecuteAsync();
        }
    }

    private DeployRequest BuildRequest() => new(
        Operation: Operation.Code,
        TargetOrg: CurrentOrg,
        SourceDir: SourceDir,
        Manifest: Manifest,
        TestLevel: TestLevel,
        Tests: Tests,
        WaitMinutes: WaitMinutes,
        JobId: JobId,
        UseMostRecent: UseMostRecent);

    private void ApplyCommandLine(string commandLine)
    {
        var args = CommandLineParser.SplitSfArguments(commandLine);
        if (args.Count >= 3 && args[0].Equals("project", StringComparison.OrdinalIgnoreCase))
        {
            var code = args[1] switch
            {
                "retrieve" => DeployOperations.Retrieve,
                _ when args[2] == "validate" => DeployOperations.Validate,
                _ when args[2] == "quick" => DeployOperations.Quick,
                _ when args[2] == "report" => DeployOperations.Report,
                _ => DeployOperations.Deploy,
            };
            Operation = Operations.FirstOrDefault(o => o.Code == code) ?? Operations[0];
        }

        var i = 3;
        while (i < args.Count)
        {
            var arg = args[i++];
            string? next = i < args.Count ? args[i] : null;
            switch (arg)
            {
                case "--source-dir" or "-d":
                    SourceDir = next;
                    i++;
                    break;
                case "--manifest" or "-x":
                    Manifest = next;
                    i++;
                    break;
                case "--test-level" or "-l":
                    TestLevel = next ?? TestLevel;
                    i++;
                    break;
                case "--tests" or "-t":
                    Tests = next;
                    i++;
                    break;
                case "--wait" or "-w":
                    if (int.TryParse(next, out var wait))
                    {
                        WaitMinutes = wait;
                    }

                    i++;
                    break;
                case "--job-id" or "-i":
                    JobId = next;
                    i++;
                    break;
                case "--use-most-recent":
                    UseMostRecent = true;
                    break;
            }
        }
    }

    private string? TrySummarize(string stdOut)
    {
        try
        {
            var jsonStart = stdOut.IndexOf('{');
            if (jsonStart < 0)
            {
                return null;
            }

            using var document = JsonDocument.Parse(stdOut[jsonStart..]);
            if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var status = result.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var id = result.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
            if (!string.IsNullOrEmpty(id))
            {
                JobId = id;
            }

            string? components = null;
            if (result.TryGetProperty("numberComponentsDeployed", out var deployed) && deployed.ValueKind == JsonValueKind.Number)
            {
                var total = result.TryGetProperty("numberComponentsTotal", out var totalElement) && totalElement.ValueKind == JsonValueKind.Number
                    ? totalElement.GetInt32()
                    : (int?)null;
                components = total is null ? UiText.T("Common_ComponentsFmt", deployed.GetInt32()) : UiText.T("Common_ComponentsOfFmt", deployed.GetInt32(), total);
            }

            var parts = new[] { status, components }.Where(p => !string.IsNullOrEmpty(p)).ToArray();
            return parts.Length == 0 ? null : string.Join(" / ", parts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void AppendHistory(string command, string status, int durationMs, string? result)
    {
        _history.Append(
            new HistoryEntry
            {
                Type = HistoryTypes.Deploy,
                Org = CurrentOrg,
                Folder = CurrentFolder,
                Params = command,
                Summary = Summarize(command),
                Status = status,
                DurationMs = durationMs,
            },
            result: result);
    }

    private static string BuildResultText(SfCliResult raw)
    {
        if (string.IsNullOrEmpty(raw.StdErr))
        {
            return raw.StdOut;
        }

        return raw.StdOut + Environment.NewLine + "--- stderr ---" + Environment.NewLine + raw.StdErr;
    }

    private static string Summarize(string text)
    {
        var single = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= 80 ? single : single[..80] + "…";
    }
}
