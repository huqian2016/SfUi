using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>デプロイ / メタデータ操作タブの ViewModel。</summary>
public partial class DeployViewModel : ObservableObject
{
    private static readonly TimeSpan ExecuteTimeout = TimeSpan.FromMinutes(60);

    private readonly DeployService _service;
    private readonly HistoryStore _history;
    private readonly FavoritesStore _favorites;
    private readonly AppSettingsStore _settings;
    private readonly AppLog _log;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private string _operation = "デプロイ (start)";

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
    private string _statusText = "準備完了";

    [ObservableProperty]
    private string? _outputText;

    [ObservableProperty]
    private HistoryEntry? _selectedHistoryItem;

    /// <summary>現在の対象組織（MainViewModel から設定される）。</summary>
    public string? CurrentOrg { get; set; }

    /// <summary>現在の SF 実行フォルダ（MainViewModel から設定される）。</summary>
    public string? CurrentFolder { get; set; }

    public ObservableCollection<string> Operations { get; } = new()
    {
        "デプロイ (start)",
        "検証 (validate)",
        "クイックデプロイ (quick)",
        "レポート (report)",
        "メタデータ取得 (retrieve)",
    };

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

    public DeployViewModel(DeployService service, HistoryStore history, FavoritesStore favorites, AppSettingsStore settings, AppLog log)
    {
        _service = service;
        _history = history;
        _favorites = favorites;
        _settings = settings;
        _log = log;
        RefreshHistory();
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
            StatusText = "上部バーで組織を選択してください";
            return;
        }

        var args = DeployService.BuildArgs(BuildRequest());
        var command = DeployService.ToDisplayCommand(args);

        if (ConfirmPolicies.ShouldConfirm(_settings.Current.ConfirmPolicy, isDangerous: true))
        {
            var answer = MessageBox.Show(
                $"デプロイ操作を実行します。よろしいですか？{Environment.NewLine}{Environment.NewLine}{command}",
                "SfUi",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                StatusText = "キャンセルしました";
                return;
            }
        }

        IsRunning = true;
        StatusText = "実行中…（完了まで出力は表示されません。「キャンセル」で中断できます）";
        OutputText = null;
        _cts = new CancellationTokenSource();
        try
        {
            var raw = await _service.ExecuteAsync(args, CurrentFolder, ExecuteTimeout, _cts.Token);
            OutputText = JsonPretty.Prettify(raw.StdOut);
            var summary = TrySummarize(raw.StdOut);
            StatusText = $"終了コード: {raw.ExitCode} / {raw.Duration.TotalMinutes:F1} 分" + (summary is null ? string.Empty : $" / {summary}");
            AppendHistory(command, raw.Success ? "success" : "error", (int)raw.Duration.TotalMilliseconds, BuildResultText(raw));
            _log.Info($"デプロイ操作: {command} → 終了コード {raw.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            StatusText = "キャンセルしました";
            AppendHistory(command, "canceled", 0, null);
        }
        catch (Exception ex)
        {
            StatusText = $"失敗: {ex.Message}";
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
        StatusText = $"お気に入りに追加: {Summarize(command)}";
    }

    [RelayCommand]
    private void BrowseSourceDir()
    {
        var dialog = new OpenFolderDialog { Title = "ソースディレクトリを選択" };
        if (!string.IsNullOrWhiteSpace(CurrentFolder) && Directory.Exists(CurrentFolder))
        {
            dialog.InitialDirectory = CurrentFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            SourceDir = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void BrowseManifest()
    {
        var dialog = new OpenFileDialog
        {
            Title = "package.xml（マニフェスト）を選択",
            Filter = "マニフェスト (*.xml)|*.xml|すべてのファイル (*.*)|*.*",
        };
        if (!string.IsNullOrWhiteSpace(CurrentFolder) && Directory.Exists(CurrentFolder))
        {
            dialog.InitialDirectory = CurrentFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            Manifest = dialog.FileName;
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
        Operation: MapOperation(Operation),
        TargetOrg: CurrentOrg,
        SourceDir: SourceDir,
        Manifest: Manifest,
        TestLevel: TestLevel,
        Tests: Tests,
        WaitMinutes: WaitMinutes,
        JobId: JobId,
        UseMostRecent: UseMostRecent);

    private static string MapOperation(string display) =>
        display.StartsWith("検証", StringComparison.Ordinal) ? DeployOperations.Validate
        : display.StartsWith("クイック", StringComparison.Ordinal) ? DeployOperations.Quick
        : display.StartsWith("レポート", StringComparison.Ordinal) ? DeployOperations.Report
        : display.StartsWith("メタデータ", StringComparison.Ordinal) ? DeployOperations.Retrieve
        : DeployOperations.Deploy;

    private void ApplyCommandLine(string commandLine)
    {
        var args = CommandLineParser.SplitSfArguments(commandLine);
        if (args.Count >= 3 && args[0].Equals("project", StringComparison.OrdinalIgnoreCase))
        {
            Operation = args[1] switch
            {
                "retrieve" => "メタデータ取得 (retrieve)",
                _ when args[2] == "validate" => "検証 (validate)",
                _ when args[2] == "quick" => "クイックデプロイ (quick)",
                _ when args[2] == "report" => "レポート (report)",
                _ => "デプロイ (start)",
            };
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
                components = total is null ? $"{deployed.GetInt32()} コンポーネント" : $"{deployed.GetInt32()}/{total} コンポーネント";
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
