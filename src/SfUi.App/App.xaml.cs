using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Services;
using SfUi.App.ViewModels;
using SfUi.App.Views;
using SfUi.Core;
using SfUi.Etl.Connections;
using SfUi.Etl.Database;
using SfUi.Etl.Engine;
using SfUi.Etl.Expressions;
using SfUi.Etl.Sources;
using SfUi.Etl.Staging;
using SfUi.Etl.Targets;
using SfUi.Etl.Transforms;
using SfUi.Etl.Verification;
using SfUi.Presentation;

namespace SfUi.App;

public partial class App : Application
{
    /// <summary>DI コンテナ（後続フェーズの各ビューから利用する）</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    private AppLog _log = null!;
    private bool _smokeTest;
    private bool _smokeAi;
    private string? _smokeOrg;
    private string? _smokeOrgInfo;
    private bool _smokeOrgInfoRefresh;
    private string? _smokeCompare;
    private string? _smokeCompareRecords;
    private string? _smokeDataIo;
    private string? _smokeAccess;
    private string? _smokeBackup;
    private string? _smokeOrgManage;
    private string? _smokeLogAnalyzer;
    private string? _smokeFieldUsageOrg;
    private string? _smokeFieldUsageObject;
    private string? _smokeFieldUsageField;
    private bool _smokeEtlRequested;
    private string? _smokeEtlOrg;
    private string? _smokeSource;
    private bool _noWelcome;
    private bool _forceWelcome;
    private bool _simulateSfMissing;
    private int _dispatcherExceptionCount;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _smokeTest = e.Args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase));
        _smokeAi = _smokeTest && e.Args.Any(a => string.Equals(a, "--smoke-ai", StringComparison.OrdinalIgnoreCase));
        _smokeOrg = _smokeTest ? ReadOption(e.Args, "--smoke-org") : null;
        _smokeOrgInfo = _smokeTest ? ReadOption(e.Args, "--smoke-orginfo") : null;
        _smokeOrgInfoRefresh = _smokeTest && e.Args.Any(a => string.Equals(a, "--smoke-orginfo-refresh", StringComparison.OrdinalIgnoreCase));
        _smokeCompare = _smokeTest ? ReadOption(e.Args, "--smoke-compare") : null;
        _smokeCompareRecords = _smokeTest ? ReadOption(e.Args, "--smoke-compare-records") : null;
        _smokeDataIo = _smokeTest ? ReadOption(e.Args, "--smoke-dataio") : null;
        _smokeAccess = _smokeTest ? ReadOption(e.Args, "--smoke-access") : null;
        _smokeBackup = _smokeTest ? ReadOption(e.Args, "--smoke-backup") : null;
        _smokeOrgManage = _smokeTest ? ReadOption(e.Args, "--smoke-orgmanage") : null;
        _smokeLogAnalyzer = _smokeTest ? ReadOption(e.Args, "--smoke-loganalyzer") : null;
        _smokeSource = _smokeTest ? ReadOption(e.Args, "--smoke-source") : null;
        if (_smokeTest)
        {
            var fieldUsageIndex = Array.FindIndex(e.Args, a => string.Equals(a, "--smoke-fieldusage", StringComparison.OrdinalIgnoreCase));
            if (fieldUsageIndex >= 0 && fieldUsageIndex + 3 < e.Args.Length)
            {
                _smokeFieldUsageOrg = e.Args[fieldUsageIndex + 1];
                _smokeFieldUsageObject = e.Args[fieldUsageIndex + 2];
                _smokeFieldUsageField = e.Args[fieldUsageIndex + 3];
            }

            _smokeEtlRequested = e.Args.Any(a =>
                string.Equals(a, "--smoke-etl", StringComparison.OrdinalIgnoreCase) ||
                a.StartsWith("--smoke-etl=", StringComparison.OrdinalIgnoreCase));
            if (_smokeEtlRequested)
            {
                _smokeEtlOrg = ReadOption(e.Args, "--smoke-etl");
                if (_smokeEtlOrg is not null && _smokeEtlOrg.StartsWith("--", StringComparison.Ordinal))
                {
                    _smokeEtlOrg = null;
                }
            }
        }

        _noWelcome = e.Args.Any(a => string.Equals(a, "--no-welcome", StringComparison.OrdinalIgnoreCase));
        _simulateSfMissing = e.Args.Any(a => string.Equals(a, "--welcome-missing", StringComparison.OrdinalIgnoreCase));
        _forceWelcome = _simulateSfMissing || e.Args.Any(a => string.Equals(a, "--welcome", StringComparison.OrdinalIgnoreCase));
        var dataDir = ReadOption(e.Args, "--data-dir") ?? Environment.GetEnvironmentVariable("SFUI_DATA_DIR");

        var paths = AppPaths.Resolve(dataDir);

        var services = new ServiceCollection();
        services.AddSfUiCore(paths);
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IFilePickerService, WpfFilePickerService>();
        services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
        services.AddSingleton<IClipboardService, WpfClipboardService>();
        services.AddSingleton<IAppWindowService, WpfAppWindowService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SoqlViewModel>();
        services.AddSingleton<ApexViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<CommandViewModel>();
        services.AddSingleton<ApiConsoleViewModel>();
        services.AddSingleton<DeployViewModel>();
        services.AddTransient<AiChatViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<QuickPanelViewModel>();
        services.AddSingleton<OrgInfoWindowFactory>();
        services.AddTransient<OrgInfoViewModel>();
        services.AddTransient<OrgInfoWindow>();
        services.AddSingleton<CompareOrgsWindowFactory>();
        services.AddTransient<CompareOrgsViewModel>();
        services.AddTransient<CompareOrgsWindow>();
        services.AddSingleton<CompareRecordDetailWindowFactory>();
        services.AddTransient<CompareRecordDetailViewModel>();
        services.AddTransient<CompareRecordDetailWindow>();
        services.AddSingleton<DataIoWindowFactory>();
        services.AddTransient<DataIoViewModel>();
        services.AddTransient<DataExportViewModel>();
        services.AddTransient<DataImportViewModel>();
        services.AddTransient<ObjectAccessViewModel>();
        services.AddTransient<FieldAccessViewModel>();
        services.AddTransient<RecordAccessViewModel>();
        services.AddTransient<DataIoWindow>();
        services.AddSingleton<EtlWindowFactory>();
        services.AddTransient<EtlViewModel>();
        services.AddTransient<EtlWindow>();
        services.AddSingleton<SourceEditorWindowFactory>();
        services.AddTransient<SourceEditorViewModel>();
        services.AddTransient<Views.SourceEditorWindow>();
        services.AddSingleton<ISourceEditorService, SourceEditorService>();
        services.AddSingleton<BackupWindowFactory>();
        services.AddTransient<BackupViewModel>();
        services.AddTransient<BackupTabViewModel>();
        services.AddTransient<RestoreTabViewModel>();
        services.AddTransient<CompareTabViewModel>();
        services.AddTransient<BackupWindow>();
        services.AddSingleton<BackupRecordsWindowFactory>();
        services.AddTransient<BackupRecordsViewModel>();
        services.AddTransient<BackupRecordsWindow>();
        services.AddSingleton<BackupCompareRecordsWindowFactory>();
        services.AddTransient<BackupCompareRecordsViewModel>();
        services.AddTransient<BackupCompareRecordsWindow>();
        services.AddSingleton<LogAnalyzerWindowFactory>();
        services.AddTransient<LogAnalyzerViewModel>();
        services.AddTransient<LogAnalyzerWindow>();
        services.AddSingleton<FieldUsageWindowFactory>();
        services.AddTransient<FieldUsageViewModel>();
        services.AddTransient<FieldUsageWindow>();
        services.AddSingleton<OrgManageWindowFactory>();
        services.AddTransient<OrgManageViewModel>();
        services.AddTransient<OrgHealthViewModel>();
        services.AddTransient<MigrationInventoryViewModel>();
        services.AddTransient<OrgManageWindow>();
        services.AddSingleton(new StartupOptions { SimulateSfMissing = _simulateSfMissing });
        services.AddTransient<WelcomeViewModel>();
        services.AddSingleton<WelcomeWindowFactory>();
        services.AddTransient<WelcomeWindow>();
        services.AddSingleton<MainWindow>();
        Services = services.BuildServiceProvider();

        _log = Services.GetRequiredService<AppLog>();
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        _log.Info($"SfUi v{version} 起動 (DataRoot={paths.DataRoot}, Smoke={_smokeTest})");
        _log.Info($"sf CLI パス: {Services.GetRequiredService<SfCliRunner>().SfExecutablePath ?? "(未検出)"}");

        // UI 言語を適用（既定は英語）
        var languageSettings = Services.GetRequiredService<AppSettingsStore>();
        UiText.SetLanguage(languageSettings.Current.Language);
        _log.Info($"UI 言語: {UiText.Language}");

        // --seed-samples: 代表的なサンプルを履歴へ投入して終了（UI は表示しない）
        if (e.Args.Any(a => string.Equals(a, "--seed-samples", StringComparison.OrdinalIgnoreCase)))
        {
            var seedResult = SampleHistorySeeder.Seed(Services.GetRequiredService<HistoryStore>(), _log);
            _log.Info($"--seed-samples: サンプル履歴を投入しました（追加 {seedResult.Added} 件 / スキップ {seedResult.Skipped} 件）");
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _log.Error("未処理例外 (AppDomain)", args.ExceptionObject as Exception);

        var window = Services.GetRequiredService<MainWindow>();
        MainWindow = window;

        // ようこそ画面（毎回表示。「今後表示しない」チェックで抑制。--no-welcome / スモーク時は表示しない）
        var showWelcome = !_smokeTest && !_noWelcome && (_forceWelcome || !languageSettings.Current.WelcomeDismissed);
        if (showWelcome)
        {
            window.ContentRendered += (_, _) => Services.GetRequiredService<MainViewModel>().ShowWelcome();
        }

        try
        {
            window.Show();
        }
        catch (Exception ex)
        {
            // レイアウト中のバインディング例外等でスタートアップ全体を中断させない
            _log.Error("MainWindow の表示中に例外が発生しました", ex);
        }

        _log.Info("MainWindow 表示");

        if (_smokeTest)
        {
            _ = RunSmokeModeAsync();
        }
    }

    /// <summary>スモーク起動: 組織一覧取得（sf CLI 実行）を確認してから自動終了する。</summary>
    private async Task RunSmokeModeAsync()
    {
        static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

        var exitCode = 0;
        try
        {
            var orgs = await Services.GetRequiredService<OrgService>().ListOrgsAsync();
            var defaultOrg = orgs.FirstOrDefault(o => o.IsDefault);
            _log.Info($"--smoke: 組織一覧取得成功: {orgs.Count} 件（既定: {defaultOrg?.DisplayName ?? "なし"}）");

            var settingsStore = Services.GetRequiredService<AppSettingsStore>();
            settingsStore.Save(); // 書込経路の確認（現値の再保存）
            var history = Services.GetRequiredService<HistoryStore>();
            var recentFolders = Services.GetRequiredService<RecentFoldersStore>();
            var favorites = Services.GetRequiredService<FavoritesStore>();
            _log.Info(
                $"--smoke: ストアOK (履歴 {history.Query().Count} 件 / 最近フォルダ {recentFolders.GetOrdered().Count} 件 / お気に入り {favorites.GetAll().Count} 件, 履歴上限 {settingsStore.Current.MaxHistoryPerType} 件 / 結果閾値 {settingsStore.Current.ResultInlineThresholdBytes} B)");
            _log.Info(
                $"--smoke: ツール検出: wt={ToolLauncherService.ResolveWindowsTerminalPath() ?? "(未検出)"}, code={ToolLauncherService.ResolveVsCodeCliPath() ?? "(未検出)"}");

            var quickPanel = Services.GetRequiredService<QuickPanelViewModel>();
            var currentSettings = settingsStore.Current;
            _log.Info(
                $"--smoke: クイックパネル: {quickPanel.Items.Count} 件 / 確認ポリシー={currentSettings.ConfirmPolicy}"
                + $" / sfパス上書き={(string.IsNullOrWhiteSpace(currentSettings.SfExecutablePath) ? "(自動検出)" : currentSettings.SfExecutablePath)}");

            // 全タブを順に選択してレイアウト（各ビューのバインディング例外を検出）
            var mainViewModel = Services.GetRequiredService<MainViewModel>();
            for (var tabIndex = 0; tabIndex < 8; tabIndex++)
            {
                mainViewModel.SelectedTabIndex = tabIndex;
                await Dispatcher.Yield(DispatcherPriority.Background);
                await Task.Delay(80);
            }

            mainViewModel.SelectedTabIndex = 0;
            _log.Info($"--smoke: 全タブのレイアウトOK (例外 {_dispatcherExceptionCount} 件)");

            // 言語切替（バインド再評価）の検証
            UiText.SetLanguage(UiText.Japanese);
            await Dispatcher.Yield(DispatcherPriority.Background);
            await Task.Delay(150);
            UiText.SetLanguage(UiText.English);
            await Dispatcher.Yield(DispatcherPriority.Background);
            await Task.Delay(150);
            UiText.SetLanguage(UiText.Chinese);
            await Dispatcher.Yield(DispatcherPriority.Background);
            await Task.Delay(150);
            UiText.SetLanguage(UiText.Korean);
            await Dispatcher.Yield(DispatcherPriority.Background);
            await Task.Delay(150);
            _log.Info($"--smoke: 言語切替OK (ja→en→zh→ko, 例外 {_dispatcherExceptionCount} 件)");

            if (!string.IsNullOrWhiteSpace(_smokeOrg))
            {
                var soqlService = Services.GetRequiredService<SoqlService>();
                var execution = await soqlService.ExecuteSoqlAsync(_smokeOrg, "SELECT Id, Name FROM Organization LIMIT 1");
                _log.Info($"--smoke: SOQL({execution.Engine}) OK: {execution.Result.TotalSize} 件 / {execution.Duration.TotalMilliseconds:F0} ms");

                var apexService = Services.GetRequiredService<ApexService>();
                var apex = await apexService.ExecuteAnonymousAsync(_smokeOrg, "System.debug('SfUi smoke test');");
                _log.Info(
                    $"--smoke: 匿名Apex 実行: success={apex.Success}, compiled={apex.Compiled}, logs={apex.Logs?.Length ?? 0} 文字"
                    + (apex.CompileProblem is { Length: > 0 } cp ? $", compileProblem={cp}" : string.Empty)
                    + (apex.ExceptionMessage is { Length: > 0 } ax ? $", exception={ax}" : string.Empty)
                    + (apex.ErrorMessage is { Length: > 0 } em ? $", error={em}" : string.Empty)
                    + (apex.Success ? string.Empty : $", raw={Truncate(apex.RawJson, 500)}"));

                var logCount = (await apexService.ListLogsAsync(_smokeOrg)).Count;
                _log.Info($"--smoke: ログ一覧: {logCount} 件");

                var console = await Services.GetRequiredService<SalesforceRestClient>()
                    .SendConsoleAsync(_smokeOrg, HttpMethod.Get, "/services/data/v67.0/limits");
                _log.Info($"--smoke: RESTコンソール GET /limits: HTTP {(int)console.StatusCode} ({console.Body.Length:N0} 文字)");
            }

            if (_smokeAi)
            {
                var aiClient = Services.GetRequiredService<AiChatClient>();
                _log.Info($"--smoke-ai: endpoint={aiClient.Endpoint} / model={aiClient.Model} / apiKey={aiClient.ApiKeySource}");
                var aiResult = await aiClient.ChatAsync(new[] { new AiChatClient.ChatMessage("user", "Reply with the single word: OK") });
                if (aiResult.Success)
                {
                    _log.Info($"--smoke-ai: OK（{Truncate(aiResult.Content?.Trim() ?? "", 80)} / {aiResult.Duration.TotalSeconds:F1} 秒 / tokens {aiResult.PromptTokens}+{aiResult.CompletionTokens}）");
                }
                else
                {
                    exitCode = 1;
                    _log.Error($"--smoke-ai: 失敗: {aiResult.Error}");
                }
            }

            if (!string.IsNullOrWhiteSpace(_smokeCompare) && !await RunCompareSmokeAsync(_smokeCompare))
            {
                exitCode = 1;
            }

            if (!string.IsNullOrWhiteSpace(_smokeCompareRecords) && !await RunCompareRecordsSmokeAsync(_smokeCompareRecords))
            {
                exitCode = 1;
            }

            if (!string.IsNullOrWhiteSpace(_smokeOrgInfo) && !await RunOrgInfoSmokeAsync(_smokeOrgInfo))
            {
                exitCode = 1;
            }

            if (!string.IsNullOrWhiteSpace(_smokeDataIo) && !await RunDataIoSmokeAsync(_smokeDataIo))
            {
                exitCode = 1;
            }

            if (!string.IsNullOrWhiteSpace(_smokeAccess) && !await RunAccessSmokeAsync(_smokeAccess))
            {
                exitCode = 1;
            }

            if (!string.IsNullOrWhiteSpace(_smokeBackup) && !await RunBackupSmokeAsync(_smokeBackup))
            {
                exitCode = 1;
            }

            if (!string.IsNullOrWhiteSpace(_smokeOrgManage) && !await RunOrgManageSmokeAsync(_smokeOrgManage))
            {
                exitCode = 1;
            }

            if (!string.IsNullOrWhiteSpace(_smokeLogAnalyzer) && !await RunLogAnalyzerSmokeAsync(_smokeLogAnalyzer))
            {
                exitCode = 1;
            }

            if (!string.IsNullOrWhiteSpace(_smokeFieldUsageOrg) && !await RunFieldUsageSmokeAsync())
            {
                exitCode = 1;
            }

            if (_smokeEtlRequested && !await RunEtlSmokeAsync(_smokeEtlOrg))
            {
                exitCode = 1;
            }

            if (!string.IsNullOrWhiteSpace(_smokeSource) && !await RunSourceEditorSmokeAsync(_smokeSource))
            {
                exitCode = 1;
            }
        }
        catch (Exception ex)
        {
            exitCode = 1;
            _log.Error("--smoke: 組織一覧の取得に失敗", ex);
        }

        if (_dispatcherExceptionCount > 0)
        {
            exitCode = 1;
            _log.Error($"--smoke: レイアウト中の未処理例外が {_dispatcherExceptionCount} 件ありました");
        }

        await Task.Delay(TimeSpan.FromSeconds(1));

        if (exitCode == 0)
        {
            _log.Info("--smoke モード: 正常に終了します");
        }
        else
        {
            _log.Info("--smoke モード: 失敗のため終了コード 1 で終了します");
        }

        Shutdown(exitCode);
    }

    /// <summary>
    /// --smoke-loganalyzer: デバッグログを解析してサマリを出力する。
    /// 引数が既存ファイルならそのファイル、そうでなければ組織 alias として直近ログを取得して解析する。
    /// </summary>
    private async Task<bool> RunLogAnalyzerSmokeAsync(string target)
    {
        try
        {
            string? logText;
            string source;
            if (File.Exists(target))
            {
                logText = await File.ReadAllTextAsync(target);
                source = $"file:{target}";
            }
            else
            {
                var apexService = Services.GetRequiredService<ApexService>();
                logText = await apexService.GetLogAsync(target);
                source = $"org:{target}";
                if (string.IsNullOrEmpty(logText))
                {
                    // 組織にデバッグログが保存されていない場合は、匿名 Apex を実行して
                    // 実行結果に含まれるログ本文を解析する（読み取り専用に近い最小コード）。
                    var run = await apexService.ExecuteAnonymousAsync(
                        target,
                        "System.debug('SfUi log analyzer smoke');\nList<Account> accounts = [SELECT Id, Name FROM Account LIMIT 5];\nSystem.debug('Accounts: ' + accounts.size());");
                    if (run.Success && !string.IsNullOrEmpty(run.Logs))
                    {
                        logText = run.Logs;
                        source = $"org-run:{target}";
                    }
                    else
                    {
                        _log.Error(
                            $"--smoke-loganalyzer: ログを取得できませんでした（匿名 Apex も失敗: {run.ErrorMessage ?? run.ExceptionMessage ?? run.CompileProblem ?? "不明"}）: {target}");
                        return false;
                    }
                }
            }

            if (string.IsNullOrEmpty(logText))
            {
                _log.Error($"--smoke-loganalyzer: ログを取得できませんでした: {target}");
                return false;
            }

            static string Cut(string text, int max) => text.Length <= max ? text : text[..max] + "…";

            var stopwatch = Stopwatch.StartNew();
            var analysis = DebugLogParser.Parse(logText);
            stopwatch.Stop();

            var summary = analysis.Summary;
            _log.Info($"--smoke-loganalyzer: {source} 解析 {logText.Length:N0} 文字 / {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
            _log.Info(
                $"--smoke-loganalyzer: 合計 {summary.TotalDurationMs:F0} ms / イベント {summary.EventCount:N0}"
                + $" / SOQL {summary.SoqlCount}({summary.SoqlRows:N0}行) / DML {summary.DmlCount}({summary.DmlRows:N0}行)"
                + $" / コールアウト {summary.CalloutCount} / 例外 {summary.Errors.Count} / リミット {summary.Limits.Count}");

            foreach (var limit in summary.Limits.Take(3))
            {
                _log.Info($"--smoke-loganalyzer: リミット {limit.Namespace} {limit.Name} {limit.Used:N0}/{limit.Max:N0} ({limit.Percent:F1}%)");
            }

            foreach (var node in DebugLogParser.GetSlowestNodes(analysis, 3))
            {
                _log.Info($"--smoke-loganalyzer: 遅い {node.EventType} {Cut(node.Label, 70)} {node.DurationMs:F0} ms");
            }

            foreach (var error in summary.Errors.Take(3))
            {
                _log.Info($"--smoke-loganalyzer: 例外 {error.Type} {Cut(error.Message, 100)} (行 {error.LineNumber})");
            }

            return summary.EventCount > 0;
        }
        catch (Exception ex)
        {
            _log.Error($"--smoke-loganalyzer: 検証に失敗しました: {target}", ex);
            return false;
        }
    }

    /// <summary>
    /// --smoke-fieldusage: 項目の使用箇所（フィールド影響分析）を実行してソース別の件数を出力する。
    /// 引数: &lt;org&gt; &lt;Object&gt; &lt;Field&gt;（例: --smoke-fieldusage hks4sand1 Account Name）。
    /// </summary>
    private async Task<bool> RunFieldUsageSmokeAsync()
    {
        try
        {
            var service = Services.GetRequiredService<FieldUsageService>();
            var result = await service.AnalyzeAsync(_smokeFieldUsageOrg!, _smokeFieldUsageObject!, _smokeFieldUsageField!);
            _log.Info($"--smoke-fieldusage: {result.ObjectApiName}.{result.FieldApiName} 使用箇所 {result.TotalCount} 件 / {result.Duration.TotalSeconds:F1} 秒");

            foreach (var kind in Enum.GetValues<FieldUsageSourceKind>())
            {
                _log.Info($"--smoke-fieldusage: {kind}: {result.CountOf(kind)} 件");
            }

            foreach (var warning in result.Warnings)
            {
                _log.Info($"--smoke-fieldusage: 警告 {warning.SourceKind}: {warning.Message}");
            }

            foreach (var hit in result.Hits.Take(5))
            {
                var location = hit.LineNumber is { } line ? $"行 {line}" : (hit.Path ?? "");
                var excerpt = hit.Excerpt.Length <= 90 ? hit.Excerpt : hit.Excerpt[..90] + "…";
                _log.Info($"--smoke-fieldusage: 例 [{hit.SourceKind}] {hit.ComponentName} {location}: {excerpt}");
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.Error("--smoke-fieldusage: 検証に失敗", ex);
            return false;
        }
    }

    /// <summary>
    /// <summary>
    /// --smoke-dataio: オブジェクト一覧（DescribeGlobal）・describe・REST エクスポート・
    /// インポート計画のドライランを検証する（書き込みなし）。
    /// </summary>
    private async Task<bool> RunDataIoSmokeAsync(string target)
    {
        try
        {
            var orgs = await Services.GetRequiredService<OrgService>().ListOrgsAsync();
            var org = orgs.FirstOrDefault(o =>
                string.Equals(o.Alias, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(o.Username, target, StringComparison.OrdinalIgnoreCase));
            if (org is null)
            {
                _log.Error($"--smoke-dataio: 組織が見つかりません: {target}");
                return false;
            }

            var describeService = Services.GetRequiredService<SObjectDescribeService>();
            var objects = await describeService.ListObjectsAsync(target);
            _log.Info($"--smoke-dataio: オブジェクト一覧 {objects.Count} 件（queryable {objects.Count(o => o.Queryable)} / createable {objects.Count(o => o.Createable)}）");

            var account = await describeService.DescribeAsync(target, "Account");
            _log.Info($"--smoke-dataio: Account の項目 {account.Fields.Count} 件（createable {account.Fields.Count(f => f.Createable)} / 必須 {account.Fields.Count(f => f.RequiredForInsert)}）");

            // REST エクスポート（少量）
            var soql = DataIoQueryBuilder.Build("Account", new[] { "Id", "Name" }, null, null, 5);
            var exportService = Services.GetRequiredService<DataExportService>();
            var export = await exportService.RunRestAsync(target, soql);
            _log.Info($"--smoke-dataio: REST エクスポート OK: {export.Result.RowCount} 行 / done={export.Result.Done} / {export.Duration.TotalMilliseconds:F0} ms");

            // インポート計画のドライラン（送信しない）: 仮の CSV 2 行 → Account Insert の計画
            var csv = CsvParser.Parse("Name,BillingCity\nSmoke Test 1,Tokyo\nSmoke Test 2,Osaka");
            var mappings = ImportFieldMatcher.Suggest(csv.Headers, account, DataImportOperation.Insert, null);
            var validation = ImportFieldMatcher.Validate(mappings, csv.RowCount, DataImportOperation.Insert, null);
            if (validation is not null)
            {
                _log.Error($"--smoke-dataio: マッピング検証に失敗: {validation}");
                return false;
            }

            var plan = ImportBatchPlanner.BuildPlan(csv.Rows, mappings, account, DataImportOperation.Insert, null, emptyAsNull: false);
            var batches = ImportBatchPlanner.ChunkSendable(plan.Rows);
            var body = ImportBatchPlanner.BuildCompositeBody(batches[0], account.Name, includeId: false);
            _log.Info($"--smoke-dataio: インポート計画ドライラン OK: {plan.Rows.Count} 行 / バッチ {batches.Count} / ペイロード {body.Length} 文字 / エラー行 {plan.Rows.Count(r => r.Error is not null)}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("--smoke-dataio: 検証に失敗しました", ex);
            return false;
        }
    }

    /// <summary>
    /// --smoke-orgmanage: 組織一覧・REST /limits・移行棚卸しを取得する（書き込みなし）。
    /// </summary>
    private async Task<bool> RunOrgManageSmokeAsync(string target)
    {
        try
        {
            var orgs = await Services.GetRequiredService<OrgService>().ListOrgsAsync();
            var org = orgs.FirstOrDefault(o =>
                string.Equals(o.Alias, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(o.Username, target, StringComparison.OrdinalIgnoreCase));
            if (org is null)
            {
                _log.Error($"--smoke-orgmanage: 組織が見つかりません: {target}");
                return false;
            }

            _log.Info($"--smoke-orgmanage: 組織一覧 {orgs.Count} 件（既定: {orgs.FirstOrDefault(o => o.IsDefault)?.DisplayName ?? "なし"}）");

            var rest = Services.GetRequiredService<SalesforceRestClient>();
            using (var limits = await rest.GetLimitsAsync(target))
            {
                var parsed = OrgLimitsParser.Parse(limits.RootElement);
                var top = parsed
                    .Where(l => l.Percent is not null)
                    .OrderByDescending(l => l.Percent)
                    .Take(3)
                    .Select(l => $"{l.Key}={l.Percent!.Value:F1}%");
                _log.Info($"--smoke-orgmanage: /limits 解析 {parsed.Count} 件（上位: {string.Join(", ", top)}）");
            }

            var inventoryService = Services.GetRequiredService<MigrationInventoryService>();
            var inventory = await inventoryService.FetchAsync(target, CancellationToken.None);
            _log.Info($"--smoke-orgmanage: 棚卸し 合計 {inventory.Total} 件"
                + $"（Workflow {inventory.WorkflowCount} / Process Builder {inventory.ProcessBuilderCount} / Flow {inventory.FlowCount} / 有効 {inventory.ActiveCount}）"
                + (inventory.WorkflowError is { Length: > 0 } error
                    ? $" / WorkflowError={(error.Length <= 120 ? error : error[..120] + "…")}"
                    : string.Empty));

            var manageService = Services.GetRequiredService<OrgManageService>();
            var connection = await manageService.TestConnectionAsync(target, CancellationToken.None);
            _log.Info($"--smoke-orgmanage: 疎通テスト success={connection.Success} / {connection.Duration.TotalMilliseconds:F0} ms"
                + $" / {connection.Detail}{(connection.Success ? string.Empty : $" / {connection.Message}")}");
            if (!connection.Success)
            {
                return false;
            }

            var stateStore = Services.GetRequiredService<OrgManageStateStore>();
            stateStore.Set(org.Username, "smoke-tag", "smoke-note");
            var entry = stateStore.Get(org.Username);
            var tagOk = entry?.Tag == "smoke-tag" && entry.Note == "smoke-note";
            stateStore.Set(org.Username, null, null);
            _log.Info($"--smoke-orgmanage: タグ・メモ round-trip {(tagOk ? "OK" : "NG")}（{stateStore.StatePath}）");
            if (!tagOk)
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.Error("--smoke-orgmanage: 検証に失敗しました", ex);
            return false;
        }
    }

    /// <summary>
    /// --smoke-backup: バックアップと復元の E2E（マーカー レコード作成 → バックアップ → 削除 →
    /// 復元1: Id 照合（削除済みは undelete で Id 維持）→ 復元2: 上書き → 復元3: キー照合（新 Id + 参照張り替え）→
    /// Bulk エンジン確認 → 後片付け）を行う。
    /// </summary>
    private async Task<bool> RunBackupSmokeAsync(string target)
    {
        const string marker = "SfUiBkE2E";
        try
        {
            var orgs = await Services.GetRequiredService<OrgService>().ListOrgsAsync();
            var org = orgs.FirstOrDefault(o =>
                string.Equals(o.Alias, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(o.Username, target, StringComparison.OrdinalIgnoreCase));
            if (org is null)
            {
                _log.Error($"--smoke-backup: 組織が見つかりません: {target}");
                return false;
            }

            var rest = Services.GetRequiredService<SalesforceRestClient>();
            var backups = Services.GetRequiredService<BackupService>();
            var settings = Services.GetRequiredService<AppSettingsStore>();
            var apiVersion = await rest.GetApiVersionAsync(target);
            var stamp = DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
            var unique = Guid.NewGuid().ToString("N")[..8];
            var accountName = $"{marker} {unique} {stamp}";
            var caseSubject = $"{marker} {unique} Child {stamp}";

            // 0) 以前の失敗で残ったマーカーを掃除（重複ルール対策）
            var cleanedCases = await DeleteMarkersAsync(rest, target, apiVersion, "Case", "Subject", marker + "%");
            var cleanedAccounts = await DeleteMarkersAsync(rest, target, apiVersion, "Account", "Name", marker + "%");
            if (cleanedCases > 0 || cleanedAccounts > 0)
            {
                _log.Info($"--smoke-backup: 残骸を掃除 Case={cleanedCases} / Account={cleanedAccounts}");
            }

            // 件数キャッシュ（並列 COUNT + キャッシュ）を検証
            await backups.FetchCountsAsync(target, org.Username, new[] { "Account", "Case" }, null, CancellationToken.None);
            var counts = backups.GetCachedCounts(org.Username);
            _log.Info($"--smoke-backup: 件数 Account={counts.GetValueOrDefault("Account")} / Case={counts.GetValueOrDefault("Case")}");

            // 1) マーカー レコード（親 + 子の参照）
            var accountId = await CreateSmokeRecordAsync(rest, target, apiVersion, "Account",
                new Dictionary<string, object?> { ["Name"] = accountName, ["BillingCity"] = "Tokyo" });
            var caseId = await CreateSmokeRecordAsync(rest, target, apiVersion, "Case",
                new Dictionary<string, object?> { ["Subject"] = caseSubject, ["AccountId"] = accountId });
            _log.Info($"--smoke-backup: 作成 Account={accountId} / Case={caseId}");

            // 2) バックアップ（REST）
            var metadata = await backups.RunBackupAsync(
                target, org.DisplayName, org.Username, org.OrgId, "smoke", "smoke backup", "smoke",
                new[] { "Account", "Case" }, null, CancellationToken.None);
            var accountInfo = metadata.Objects.First(o => o.Name == "Account");
            var caseInfo = metadata.Objects.First(o => o.Name == "Case");
            _log.Info($"--smoke-backup: バックアップ {metadata.Id}（Account {accountInfo.Count} / Case {caseInfo.Count} / Engine {accountInfo.Engine}）");
            if (accountInfo.Count == 0 || caseInfo.Count == 0)
            {
                _log.Error("--smoke-backup: バックアップ件数が 0 です");
                return false;
            }

            var records = await backups.LoadRecordsAsync(metadata.Id, accountInfo);
            if (records.Rows.Count == 0 || !records.Columns.Contains("Id"))
            {
                _log.Error("--smoke-backup: レコード詳細の読み込みに失敗しました");
                return false;
            }

            _log.Info($"--smoke-backup: レコード詳細 {records.Rows.Count} 件 / 列 {records.Columns.Count}");

            // 3) 削除（子 → 親）
            await DeleteSmokeRecordAsync(rest, target, apiVersion, "Case", caseId);
            await DeleteSmokeRecordAsync(rest, target, apiVersion, "Account", accountId);

            // 4) 復元1: Id 照合 + スキップ → 削除済みは undelete（Id 維持）
            var summary1 = await backups.RunRestoreAsync(
                target, org.OrgId, metadata, new[] { accountInfo, caseInfo },
                new RestoreOptions(RestoreMatchMode.Id, RestoreExistingAction.Skip, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)),
                null, CancellationToken.None);
            _log.Info($"--smoke-backup: 復元1(Id) 作成 {summary1.Created} / 上書き {summary1.Updated} / 復元 {summary1.Undeleted} / スキップ {summary1.Skipped} / 失敗 {summary1.Failed}");
            var restoredCaseId = await QueryFieldAsync(rest, target, $"SELECT Id FROM Case WHERE Subject = '{caseSubject}'", "Id");
            // 子（Case）は親の削除時にカスケード削除扱いとなり個別 undelete 不可 → insert フォールバックを許容
            _log.Info($"--smoke-backup: Account は {((summary1.Undeleted > 0 && await ExistsAsync(rest, target, "Account", accountId)) ? "undelete（Id 維持）" : "未復元")} / Case は {(summary1.Created > 0 ? "insert フォールバック" : "undelete")}");
            if (summary1.Undeleted < 1 || summary1.Failed > 0 || !await ExistsAsync(rest, target, "Account", accountId) || restoredCaseId is null)
            {
                _log.Error("--smoke-backup: undelete（Id 維持）の検証に失敗しました");
                return false;
            }

            // 5) 復元2: Id 照合 + 上書き → 変更した項目がバックアップ値へ戻る
            await UpdateSmokeRecordAsync(rest, target, apiVersion, "Account", accountId,
                new Dictionary<string, object?> { ["BillingCity"] = "Osaka" });
            var summary2 = await backups.RunRestoreAsync(
                target, org.OrgId, metadata, new[] { accountInfo },
                new RestoreOptions(RestoreMatchMode.Id, RestoreExistingAction.Overwrite, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)),
                null, CancellationToken.None);
            var city = await QueryFieldAsync(rest, target, $"SELECT BillingCity FROM Account WHERE Id = '{accountId}'", "BillingCity");
            _log.Info($"--smoke-backup: 復元2(上書き) 上書き {summary2.Updated} → BillingCity={city}");
            if (summary2.Updated < 1 || !string.Equals(city, "Tokyo", StringComparison.Ordinal))
            {
                _log.Error("--smoke-backup: 上書き復元の検証に失敗しました");
                return false;
            }

            // 6) 復元3: キー照合（Name / Subject）→ 新 Id で作成 + 参照の張り替え
            await DeleteSmokeRecordAsync(rest, target, apiVersion, "Case", restoredCaseId);
            await DeleteSmokeRecordAsync(rest, target, apiVersion, "Account", accountId);
            var keyFields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Account"] = "Name", ["Case"] = "Subject" };
            var summary3 = await backups.RunRestoreAsync(
                target, org.OrgId, metadata, new[] { accountInfo, caseInfo },
                new RestoreOptions(RestoreMatchMode.Key, RestoreExistingAction.Skip, keyFields),
                null, CancellationToken.None);
            var newAccountId = await QueryFieldAsync(rest, target, $"SELECT Id FROM Account WHERE Name = '{accountName}'", "Id");
            var newCaseId = await QueryFieldAsync(rest, target, $"SELECT Id FROM Case WHERE Subject = '{caseSubject}'", "Id");
            var newCaseAccount = newCaseId is null
                ? null
                : await QueryFieldAsync(rest, target, $"SELECT AccountId FROM Case WHERE Id = '{newCaseId}'", "AccountId");
            _log.Info($"--smoke-backup: 復元3(キー) 作成 {summary3.Created} → Account={newAccountId} / Case={newCaseId} / Case.AccountId={newCaseAccount}");
            if (newAccountId is null || newCaseId is null || newAccountId == accountId
                || !string.Equals(newCaseAccount, newAccountId, StringComparison.Ordinal))
            {
                _log.Error("--smoke-backup: キー照合（新 Id・参照張り替え）の検証に失敗しました");
                return false;
            }

            // 7) Bulk エンジン（しきい値を 1 にして CSV 保存を確認）
            var originalThreshold = settings.Current.BackupRestMaxRecords;
            try
            {
                settings.Current.BackupRestMaxRecords = 1;
                var bulkMetadata = await backups.RunBackupAsync(
                    target, org.DisplayName, org.Username, org.OrgId, "smoke", "smoke bulk", "smoke",
                    new[] { "Account" }, null, CancellationToken.None);
                var bulkInfo = bulkMetadata.Objects.First(o => o.Name == "Account");
                var bulkRecords = await backups.LoadRecordsAsync(bulkMetadata.Id, bulkInfo);
                _log.Info($"--smoke-backup: Bulk バックアップ {bulkMetadata.Id}（Engine {bulkInfo.Engine} / ファイル {bulkInfo.File} / 行 {bulkRecords.Rows.Count}）");
                if (bulkInfo.Engine != BackupEngine.Bulk || bulkRecords.Rows.Count == 0)
                {
                    _log.Error("--smoke-backup: Bulk エンジンの検証に失敗しました");
                    return false;
                }
            }
            finally
            {
                settings.Current.BackupRestMaxRecords = originalThreshold;
                settings.Save();
            }

            // 8) 後片付け（子 → 親、マーカー全体を掃除）
            await DeleteMarkersAsync(rest, target, apiVersion, "Case", "Subject", marker + "%");
            await DeleteMarkersAsync(rest, target, apiVersion, "Account", "Name", marker + "%");
            _log.Info("--smoke-backup: 後片付け完了");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("--smoke-backup: 検証に失敗しました", ex);
            return false;
        }
    }

    private static async Task<string> CreateSmokeRecordAsync(
        SalesforceRestClient rest, string org, string apiVersion, string objectName, Dictionary<string, object?> fields)
    {
        var body = await rest.SendRawAsync(
            org, HttpMethod.Post, $"/services/data/v{apiVersion}/sobjects/{objectName}", JsonSerializer.Serialize(fields));
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("Id が返りませんでした");
    }

    /// <summary>LIKE パターンに一致するレコードを削除する（マーカーの掃除）。</summary>
    private static async Task<int> DeleteMarkersAsync(
        SalesforceRestClient rest, string org, string apiVersion, string objectName, string field, string pattern)
    {
        using var document = await rest.QueryAsync(org, $"SELECT Id FROM {objectName} WHERE {field} LIKE '{pattern}'");
        var ids = document.RootElement.GetProperty("records").EnumerateArray()
            .Select(record => record.GetProperty("Id").GetString())
            .Where(id => id is not null)
            .ToList();
        foreach (var id in ids)
        {
            await DeleteSmokeRecordAsync(rest, org, apiVersion, objectName, id!);
        }

        return ids.Count;
    }

    private static Task<string> DeleteSmokeRecordAsync(
        SalesforceRestClient rest, string org, string apiVersion, string objectName, string id) =>
        rest.SendRawAsync(org, HttpMethod.Delete, $"/services/data/v{apiVersion}/sobjects/{objectName}/{id}");

    private static Task<string> UpdateSmokeRecordAsync(
        SalesforceRestClient rest, string org, string apiVersion, string objectName, string id, Dictionary<string, object?> fields) =>
        rest.SendRawAsync(org, HttpMethod.Patch, $"/services/data/v{apiVersion}/sobjects/{objectName}/{id}", JsonSerializer.Serialize(fields));

    private static async Task<bool> ExistsAsync(SalesforceRestClient rest, string org, string objectName, string id)
    {
        using var document = await rest.QueryAsync(org, $"SELECT Id FROM {objectName} WHERE Id = '{id}'");
        return document.RootElement.GetProperty("records").GetArrayLength() > 0;
    }

    private static async Task<string?> QueryFieldAsync(SalesforceRestClient rest, string org, string soql, string field)
    {
        using var document = await rest.QueryAsync(org, soql);
        var records = document.RootElement.GetProperty("records");
        if (records.GetArrayLength() == 0)
        {
            return null;
        }

        return records[0].TryGetProperty(field, out var element) && element.ValueKind != JsonValueKind.Null
            ? element.GetString()
            : null;
    }

    /// <summary>
    /// --smoke-access: 権限カタログ・オブジェクト/項目アクセス・レコードアクセス（UserRecordAccess）を検証する（読み取りのみ）。
    /// </summary>
    private async Task<bool> RunAccessSmokeAsync(string target)
    {
        try
        {
            var orgs = await Services.GetRequiredService<OrgService>().ListOrgsAsync();
            var org = orgs.FirstOrDefault(o =>
                string.Equals(o.Alias, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(o.Username, target, StringComparison.OrdinalIgnoreCase));
            if (org is null)
            {
                _log.Error($"--smoke-access: 組織が見つかりません: {target}");
                return false;
            }

            var permissions = Services.GetRequiredService<PermissionAccessService>();
            var catalog = await permissions.GetCatalogAsync(target);
            _log.Info(
                $"--smoke-access: 権限カタログ {catalog.Subjects.Count} 件（プロファイル {catalog.Subjects.Count(s => s.Kind == PermissionSubjectKind.Profile)}" +
                $" / 権限セット {catalog.Subjects.Count(s => s.Kind == PermissionSubjectKind.PermissionSet)}" +
                $" / PSG {catalog.Subjects.Count(s => s.Kind == PermissionSubjectKind.PermissionSetGroup)} / 構成 {catalog.GroupComponents.Count} グループ）");

            var objectRows = await permissions.GetObjectAccessAsync(target, "Account");
            _log.Info(
                $"--smoke-access: オブジェクトアクセス(Account) {objectRows.Count} 行（読取 {objectRows.Count(r => r.Read)} / 作成 {objectRows.Count(r => r.Create)}" +
                $" / 更新 {objectRows.Count(r => r.Edit)} / 参照すべて {objectRows.Count(r => r.ViewAllRecords)} / 更新すべて {objectRows.Count(r => r.ModifyAllRecords)}）");

            var fieldAccess = await permissions.GetFieldAccessAsync(target, "Account");
            _log.Info(
                $"--smoke-access: 項目アクセス(Account) 主体 {fieldAccess.BySubject.Count} 件（項目行あり {fieldAccess.BySubject.Count(p => p.Value.Count > 0)}" +
                $" / Name 権限あり {fieldAccess.BySubject.Count(p => p.Value.ContainsKey("Account.Name"))}）");

            var recordAccess = Services.GetRequiredService<RecordAccessService>();
            var users = await recordAccess.ListActiveUsersAsync(target);
            _log.Info($"--smoke-access: 有効ユーザー {users.Count} 件");

            var query = await recordAccess.QueryRecordsAsync(target, "SELECT Id, Name FROM Account LIMIT 5");
            _log.Info($"--smoke-access: 対象レコード {query.Records.Count} 件 / 列 {string.Join(",", query.Columns)} / truncated={query.Truncated}");

            if (users.Count > 0)
            {
                var ids = query.Records
                    .Select(r => r.TryGetValue("Id", out var id) ? id : null)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => id!)
                    .ToList();
                if (ids.Count > 0)
                {
                    var flags = await recordAccess.GetAccessFlagsAsync(target, users[0].Id, ids);
                    _log.Info(
                        $"--smoke-access: UserRecordAccess {users[0].Name} × {ids.Count} 件 → {flags.Count} 行（読取あり {flags.Values.Count(f => f.Read)} / 編集あり {flags.Values.Count(f => f.Edit)}）");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.Error("--smoke-access: 検証に失敗しました", ex);
            return false;
        }
    }

    /// <summary>
    /// --smoke-compare: 指定組織（カンマ区切り・2 件以上）で全比較カテゴリを構築し、行数と差分件数をログ出力する。
    /// キャッシュ優先・未取得分は自動取得（組織情報ウィンドウと同じキャッシュを共有）。
    /// </summary>
    private async Task<bool> RunCompareSmokeAsync(string targets)
    {
        try
        {
            var orgs = await Services.GetRequiredService<OrgService>().ListOrgsAsync();
            var selected = new List<OrgInfo>();
            foreach (var target in targets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var org = orgs.FirstOrDefault(o =>
                    string.Equals(o.Alias, target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(o.Username, target, StringComparison.OrdinalIgnoreCase));
                if (org is null)
                {
                    _log.Error($"--smoke-compare: 組織が見つかりません: {target}");
                    return false;
                }

                selected.Add(org);
            }

            if (selected.Count < 2)
            {
                _log.Error("--smoke-compare: 組織は 2 つ以上指定してください（例: --smoke-compare hks4sand1,acc）");
                return false;
            }

            _log.Info($"--smoke-compare: 対象 = {string.Join(" / ", selected.Select(o => o.DisplayName))}");
            var service = Services.GetRequiredService<OrgCompareService>();
            var progress = new Progress<string>(message => _log.Info($"--smoke-compare: {message}"));

            foreach (var category in OrgCompareCategories.All)
            {
                var table = await service.BuildAsync(selected, category, forceRefresh: false, fetchMissing: true, progress);
                _log.Info($"--smoke-compare: {category.Id}: 全 {table.Rows.Count} 行 / 差分 {table.DiffCount} 行");
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.Error("--smoke-compare: 比較に失敗しました", ex);
            return false;
        }
    }

    /// <summary>
    /// --smoke-compare-records: 指定組織で Account のレコード比較（照合キー Name / Name・Industry・Type・上限 100）を
    /// 実行し、行数と差分件数をログ出力する（レコード比較タブの実 API 検証）。
    /// </summary>
    private async Task<bool> RunCompareRecordsSmokeAsync(string targets)
    {
        try
        {
            var orgs = await Services.GetRequiredService<OrgService>().ListOrgsAsync();
            var selected = new List<OrgInfo>();
            foreach (var target in targets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var org = orgs.FirstOrDefault(o =>
                    string.Equals(o.Alias, target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(o.Username, target, StringComparison.OrdinalIgnoreCase));
                if (org is null)
                {
                    _log.Error($"--smoke-compare-records: 組織が見つかりません: {target}");
                    return false;
                }

                selected.Add(org);
            }

            if (selected.Count < 2)
            {
                _log.Error("--smoke-compare-records: 組織は 2 つ以上指定してください（例: --smoke-compare-records hks4sand1,acc）");
                return false;
            }

            _log.Info($"--smoke-compare-records: 対象 = {string.Join(" / ", selected.Select(o => o.DisplayName))}");
            var service = Services.GetRequiredService<OrgRecordCompareService>();
            var request = new OrgRecordCompareRequest(
                "Account",
                "Name",
                new[]
                {
                    new OrgRecordCompareField("Name", "Name"),
                    new OrgRecordCompareField("Industry", "Industry"),
                    new OrgRecordCompareField("Type", "Type"),
                },
                Limit: 100);

            var progress = new Progress<string>(message => _log.Info($"--smoke-compare-records: {message}"));
            var results = await service.QueryAllAsync(selected, request, progress);
            foreach (var result in results)
            {
                _log.Info($"--smoke-compare-records: {result.OrgKey}: {result.State} / {result.Records.Count} 件");
            }

            var columns = selected
                .Select(o => new OrgCompareOrgColumn(OrgInfoCacheStore.GetOrgKey(o), o.DisplayName, o.Username, o.InstanceUrl))
                .ToList();
            var table = OrgRecordCompareService.BuildTable(OrgCompareCategories.CreateRecords("Account"), columns, results, request);
            _log.Info($"--smoke-compare-records: 全 {table.Rows.Count} 行 / 差分 {table.DiffCount} 行");
            if (table.Rows.Count > 0)
            {
                var sample = table.Rows.First(r => r.Cells.Any(c => c.State == OrgCompareCellState.Value));
                _log.Info($"--smoke-compare-records: 例 [{sample.Label}] {string.Join(" || ", sample.Cells.Select(OrgCompareService.CellText))}");
            }

            var anyValue = results.Any(r => r.State == OrgCompareCellState.Value);
            if (!anyValue)
            {
                _log.Error("--smoke-compare-records: 全組織でレコードを取得できませんでした");
            }

            return anyValue;
        }
        catch (Exception ex)
        {
            _log.Error("--smoke-compare-records: レコード比較に失敗しました", ex);
            return false;
        }
    }

    /// --smoke-orginfo: 組織情報の「初回のみ取得・2 回目以降は API を呼ばない・手動再取得で fetchedAt 更新」を
    /// ログで検証できる形で実行する（概要 + ユーザーの 2 セクション。--smoke-orginfo-refresh で再取得も実行）。
    /// </summary>
    private async Task<bool> RunOrgInfoSmokeAsync(string target)
    {
        try
        {
            var orgs = await Services.GetRequiredService<OrgService>().ListOrgsAsync();
            var org = orgs.FirstOrDefault(o =>
                string.Equals(o.Alias, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(o.Username, target, StringComparison.OrdinalIgnoreCase));
            if (org is null)
            {
                _log.Error($"--smoke-orginfo: 組織が見つかりません: {target}");
                return false;
            }

            var cache = Services.GetRequiredService<OrgInfoCacheStore>();
            var service = Services.GetRequiredService<OrgInfoService>();
            var orgKey = OrgInfoCacheStore.GetOrgKey(org);
            _log.Info($"--smoke-orginfo: 対象={org.DisplayName} / orgKey={orgKey}");

            var fetched = new List<string>();
            var cached = new List<string>();
            foreach (var sectionId in new[] { OrgInfoSections.Overview, OrgInfoSections.Users })
            {
                var existing = cache.GetSection(orgKey, sectionId);
                if (existing is not null)
                {
                    cached.Add(sectionId);
                    _log.Info($"--smoke-orginfo: {sectionId} はキャッシュを使用 (fetchedAt={existing.FetchedAt:yyyy-MM-dd HH:mm:ss}) → API 呼び出しなし");
                    continue;
                }

                var section = await service.FetchSectionAsync(org, sectionId);
                cache.UpsertSection(orgKey, section);
                fetched.Add(sectionId);
                _log.Info($"--smoke-orginfo: {sectionId} を取得 ({section.Rows.Count} 行 / fetchedAt={section.FetchedAt:yyyy-MM-dd HH:mm:ss})");
            }

            var fetchedText = fetched.Count == 0 ? "（キャッシュのみ）" : " (" + string.Join(", ", fetched) + ")";
            _log.Info($"--smoke-orginfo: API 呼び出し = {fetched.Count} セクション{fetchedText} / キャッシュ利用 = {cached.Count} セクション");

            if (_smokeOrgInfoRefresh)
            {
                var before = cache.GetSection(orgKey, OrgInfoSections.Overview)?.FetchedAt;
                var refreshed = await service.FetchSectionAsync(org, OrgInfoSections.Overview);
                cache.UpsertSection(orgKey, refreshed);
                var updated = before is null || refreshed.FetchedAt > before;
                _log.Info($"--smoke-orginfo: 手動再取得 overview: fetchedAt {before:yyyy-MM-dd HH:mm:ss} → {refreshed.FetchedAt:yyyy-MM-dd HH:mm:ss} (updated={updated})");
                if (!updated)
                {
                    _log.Error("--smoke-orginfo: 再取得後も fetchedAt が更新されていません");
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.Error("--smoke-orginfo: 失敗", ex);
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info($"SfUi 終了 (ExitCode={e.ApplicationExitCode})");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _dispatcherExceptionCount++;
        _log.Error("未処理例外 (Dispatcher)", e.Exception);
        if (!_smokeTest)
        {
            MessageBox.Show(
                UiText.T("Common_UnexpectedErrorFmt", Environment.NewLine, e.Exception.Message),
                "SfUi",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        e.Handled = true;
    }

    /// <summary>
    /// --smoke-source &lt;org&gt;: ソース エディタの検証。
    /// 4 種別の列挙（Tooling REST）、Apex クラス / LWC バンドルの本文取得、および
    /// 反映の往復（一時クラスを作成 → 検証のみ → 構文エラー検出 → 更新反映 → ドラフト → 削除）を実組織で行う。
    /// </summary>
    private async Task<bool> RunSourceEditorSmokeAsync(string org)
    {
        var success = true;
        try
        {
            var service = Services.GetRequiredService<ISourceEditorService>();
            var counts = new Dictionary<SourceMemberKind, int>();
            foreach (var kind in new[]
                     {
                         SourceMemberKind.ApexClass, SourceMemberKind.ApexTrigger,
                         SourceMemberKind.VisualforcePage, SourceMemberKind.LightningComponentBundle,
                     })
            {
                var members = await service.ListAsync(org, kind);
                counts[kind] = members.Count;
                _log.Info($"--smoke-source: {kind} = {members.Count} 件");
            }

            if (counts[SourceMemberKind.ApexClass] == 0)
            {
                success = false;
                _log.Error("--smoke-source: Apex クラスが 0 件です");
            }
            else
            {
                var members = await service.ListAsync(org, SourceMemberKind.ApexClass);
                var files = await service.GetSourceAsync(org, members[0]);
                var text = files.Count > 0 ? files[0].Text : string.Empty;
                var ok = files.Count == 1 && text.Length > 10 && text.Contains("class", StringComparison.OrdinalIgnoreCase);
                _log.Info($"--smoke-source: {members[0].Name} 本文 = {files.Count} ファイル / {text.Length} 文字 / 検証={ok}");
                if (!ok)
                {
                    success = false;
                    _log.Error("--smoke-source: Apex クラス本文の検証に失敗しました");
                }
            }

            if (counts[SourceMemberKind.LightningComponentBundle] == 0)
            {
                success = false;
                _log.Error("--smoke-source: LWC が 0 件です");
            }
            else
            {
                var members = await service.ListAsync(org, SourceMemberKind.LightningComponentBundle);
                var verified = false;
                foreach (var member in members.Take(5))
                {
                    var files = await service.GetSourceAsync(org, member);
                    if (files.Count < 2)
                    {
                        continue;
                    }

                    var ok = files.Any(f => f.FileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
                    _log.Info($"--smoke-source: LWC {member.Name} = {string.Join(", ", files.Select(f => f.FileName))} / 検証={ok}");
                    if (!ok)
                    {
                        success = false;
                        _log.Error("--smoke-source: LWC のファイル構成の検証に失敗しました");
                    }

                    verified = true;
                    break;
                }

                if (!verified)
                {
                    success = false;
                    _log.Error("--smoke-source: 検証可能な LWC バンドル（複数ファイル）が見つかりません");
                }
            }

            // ---- 反映の往復（作成 → 取得 → 検証のみ → 構文エラー → 更新反映 → ドラフト → 削除）----
            // 前回失敗の残骸があれば先に削除する
            foreach (var leftover in (await service.ListAsync(org, SourceMemberKind.ApexClass))
                         .Where(m => m.Name.StartsWith("SfUiSmokeSrc", StringComparison.Ordinal)))
            {
                await service.DeleteAsync(org, leftover);
            }

            var smokeName = "SfUiSmokeSrc" + DateTime.Now.ToString("HHmmss");
            var smokeMember = new SourceMemberInfo(SourceMemberKind.ApexClass, smokeName, string.Empty);
            // 新規作成と同じテンプレート経路で作成する
            var create = await service.DeployAsync(org, smokeMember, SourceEditorTemplates.BuildFiles(SourceMemberKind.ApexClass, smokeName), dryRun: false);
            _log.Info($"--smoke-source: [deploy] 作成 = {create.Success}（{create.Message}）");
            if (!create.Success)
            {
                success = false;
            }

            var created = (await service.ListAsync(org, SourceMemberKind.ApexClass)).FirstOrDefault(m => m.Name == smokeName);
            if (created is null)
            {
                success = false;
                _log.Error("--smoke-source: [deploy] 作成したクラスが一覧に見つかりません");
            }
            else
            {
                var body = await service.GetSourceAsync(org, created);
                var bodyOk = body.Count == 1
                             && body[0].Text.Contains("public with sharing class " + smokeName, StringComparison.Ordinal);
                _log.Info($"--smoke-source: [deploy] 作成後の取得 = {bodyOk}");
                if (!bodyOk)
                {
                    success = false;
                }

                var dry = await service.DeployAsync(org, created, SourceEditorTemplates.BuildFiles(SourceMemberKind.ApexClass, smokeName), dryRun: true);
                _log.Info($"--smoke-source: [deploy] 検証のみ = {dry.Success}");
                if (!dry.Success)
                {
                    success = false;
                }

                var bad = $"public with sharing class {smokeName} {{ public static String Hi() {{ int a = ; }} }}";
                var failed = await service.DeployAsync(org, created, new[] { new SourceFileInfo(smokeName + ".cls", "C#", bad) }, dryRun: false);
                var lineOk = !failed.Success && failed.Errors.Count > 0 && failed.Errors[0].Line > 0;
                _log.Info($"--smoke-source: [deploy] 構文エラー検出 = {lineOk}（{failed.Errors.Count} 件 / 先頭行 {failed.Errors.FirstOrDefault()?.Line}）");
                if (!lineOk)
                {
                    success = false;
                }

                var v2 = $"public with sharing class {smokeName} {{ public static String Hi() {{ return 'sfui-v2'; }} }}";
                var update = await service.DeployAsync(org, created, new[] { new SourceFileInfo(smokeName + ".cls", "C#", v2) }, dryRun: false);
                var body2 = await service.GetSourceAsync(org, created);
                var updatedOk = update.Success && body2.Count == 1 && body2[0].Text.Contains("sfui-v2", StringComparison.Ordinal);
                _log.Info($"--smoke-source: [deploy] 更新反映 = {updatedOk}");
                if (!updatedOk)
                {
                    success = false;
                }

                var orgKey = SourceEditorWorkspace.KeyFor("smoke");
                await service.SaveDraftAsync(orgKey, created, body2, new[] { new SourceFileInfo(smokeName + ".cls", "C#", body2[0].Text + "\n// draft") });
                var draft = await service.LoadDraftAsync(orgKey, created);
                var draftOk = draft is not null && draft.Working[0].Text.EndsWith("// draft", StringComparison.Ordinal);
                _log.Info($"--smoke-source: [deploy] ドラフト往復 = {draftOk}");
                if (!draftOk)
                {
                    success = false;
                }

                await service.ClearDraftAsync(orgKey, created);

                var deleted = await service.DeleteAsync(org, created);
                var stillThere = (await service.ListAsync(org, SourceMemberKind.ApexClass)).Any(m => m.Name == smokeName);
                var deletedOk = deleted.Success && !stillThere;
                _log.Info($"--smoke-source: [deploy] 削除 = {deletedOk}");
                if (!deletedOk)
                {
                    success = false;
                }
            }

            // ---- LWC バンドルの作成 → 取得 → 削除（テンプレート + バンドル deploy + 削除 source）----
            foreach (var leftover in (await service.ListAsync(org, SourceMemberKind.LightningComponentBundle))
                         .Where(m => m.Name.StartsWith("sfuiSmoke", StringComparison.Ordinal)))
            {
                await service.DeleteAsync(org, leftover);
            }

            var lwcName = "sfuiSmoke" + DateTime.Now.ToString("HHmmss");
            var lwcMember = new SourceMemberInfo(SourceMemberKind.LightningComponentBundle, lwcName, string.Empty);
            var lwcCreate = await service.DeployAsync(org, lwcMember, SourceEditorTemplates.BuildFiles(SourceMemberKind.LightningComponentBundle, lwcName), dryRun: false);
            _log.Info($"--smoke-source: [deploy] LWC 作成 = {lwcCreate.Success}（{lwcCreate.Message}）");
            if (!lwcCreate.Success)
            {
                success = false;
            }
            else
            {
                var lwcListed = (await service.ListAsync(org, SourceMemberKind.LightningComponentBundle)).FirstOrDefault(m => m.Name == lwcName);
                if (lwcListed is null)
                {
                    success = false;
                    _log.Error("--smoke-source: [deploy] 作成した LWC が一覧に見つかりません");
                }
                else
                {
                    var lwcFiles = await service.GetSourceAsync(org, lwcListed);
                    var lwcOk = lwcFiles.Count == 4
                                && lwcFiles.Any(f => f.FileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                                && lwcFiles.Any(f => f.FileName.EndsWith(".js-meta.xml", StringComparison.OrdinalIgnoreCase));
                    _log.Info($"--smoke-source: [deploy] LWC 作成後の取得 = {lwcOk}（{string.Join(", ", lwcFiles.Select(f => f.FileName))}）");
                    if (!lwcOk)
                    {
                        success = false;
                    }

                    var lwcDeleted = await service.DeleteAsync(org, lwcListed);
                    var lwcGone = !(await service.ListAsync(org, SourceMemberKind.LightningComponentBundle)).Any(m => m.Name == lwcName);
                    var lwcDeletedOk = lwcDeleted.Success && lwcGone;
                    _log.Info($"--smoke-source: [deploy] LWC 削除 = {lwcDeletedOk}");
                    if (!lwcDeletedOk)
                    {
                        success = false;
                    }
                }
            }

            // ---- フロー グラフ（一覧 = REST / バージョン・Metadata = Tooling）----
            var flows = await service.ListFlowsAsync(org);
            _log.Info($"--smoke-source: [flow] 一覧 = {flows.Count} 件");
            if (flows.Count == 0)
            {
                success = false;
                _log.Error("--smoke-source: [flow] フローが 0 件です");
            }
            else
            {
                FlowGraph? graph = null;
                var inspected = 0;
                foreach (var flow in flows.Take(30))
                {
                    FlowGraph candidate;
                    try
                    {
                        candidate = await service.GetFlowGraphAsync(org, flow);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Info($"--smoke-source: [flow] {flow.Name} は読み込めないためスキップ（{ex.Message}）");
                        continue;
                    }

                    inspected++;
                    if (candidate.Nodes.Count >= 5 && candidate.Edges.Count > 0)
                    {
                        graph = candidate;
                        break;
                    }
                }

                if (graph is null)
                {
                    success = false;
                    _log.Error($"--smoke-source: [flow] 5 ノード以上のフローが見つかりません（{inspected} 件確認）");
                }
                else
                {
                    var ids = graph.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
                    var endpointsOk = graph.Edges.All(e => ids.Contains(e.FromId) && ids.Contains(e.ToId));
                    var startOk = graph.Nodes.Any(n => n.Category == "Start");
                    var labelsOk = graph.Nodes.All(n => !string.IsNullOrWhiteSpace(n.Label))
                                   && !string.IsNullOrWhiteSpace(graph.Label)
                                   && !string.IsNullOrWhiteSpace(graph.VersionLabel);
                    var details = graph.Nodes.Count(n => n.Details.Count > 0);
                    _log.Info($"--smoke-source: [flow] {graph.Label}（{graph.VersionLabel}）= {graph.Nodes.Count} ノード / {graph.Edges.Count} 接続 / 詳細付き {details} / 端点={endpointsOk} / Start={startOk} / ラベル={labelsOk}");
                    if (!endpointsOk || !startOk || !labelsOk)
                    {
                        success = false;
                        _log.Error("--smoke-source: [flow] グラフの検証に失敗しました");
                    }

                    var categories = string.Join(", ", graph.Nodes
                        .GroupBy(n => n.Category)
                        .OrderByDescending(g => g.Count())
                        .Select(g => $"{g.Key}×{g.Count()}"));
                    _log.Info($"--smoke-source: [flow] カテゴリ = {categories}");
                }
            }
        }
        catch (Exception ex)
        {
            success = false;
            _log.Error("--smoke-source: 検証に失敗", ex);
        }

        _log.Info(success ? "--smoke-source: OK" : "--smoke-source: NG");
        return success;
    }

    /// <summary>
    /// --smoke-etl [org]: ETL の検証。オフライン（CSV → マッピング → CSV 出力 + dry-run）を常に実行し、
    /// 組織指定時は Contact への小規模適用（1 行を意図的に失敗させてエラー率停止 → ロールバック →
    /// 全行成功 → 成功後巻き戻し）を行う。
    /// </summary>
    private async Task<bool> RunEtlSmokeAsync(string? org)
    {
        var success = true;
        try
        {
            var paths = Services.GetRequiredService<AppPaths>();
            var tmp = Path.Combine(paths.TempDirectory, "etl-smoke");
            Directory.CreateDirectory(tmp);

            // ---- オフライン: CSV → マッピング → CSV（dry-run → 実行）----
            var csvIn = Path.Combine(tmp, "in.csv");
            var csvOut = Path.Combine(tmp, "out.csv");
            File.WriteAllText(csvIn, "Id,Name\r\n1,Alpha\r\n2,Beta\r\n3,Gamma\r\n", new UTF8Encoding(false));
            if (File.Exists(csvOut))
            {
                File.Delete(csvOut);
            }

            var offlineDir = Path.Combine(paths.EtlRunsRoot, "run-smoke-offline");
            using (var store = RunStagingStore.Create(offlineDir, "run-smoke-offline"))
            {
                var source = new CsvFileSource(csvIn);
                var mapper = new RowMapper(
                    source.Columns,
                    new[] { new FieldMapping("Id", "[Id]"), new FieldMapping("Name", "[Name]") },
                    new ExpressionEngine());
                var plan = new EtlStepPlan
                {
                    StepId = "step1",
                    ObjectName = "Offline",
                    Source = source,
                    Mapper = mapper,
                    Target = new CsvFileTarget(csvOut, new[] { "Id", "Name" }),
                };
                var step = new EtlStepRun(store, plan);
                step.Prepare();

                var dry = await step.ApplyAsync(dryRun: true);
                var dryOk = dry.StopReason == "dry-run" && dry.Pending == 3 && !File.Exists(csvOut);
                _log.Info($"--smoke-etl: [offline] dry-run pending={dry.Pending}（期待 3）/ 出力なし={!File.Exists(csvOut)}");
                if (!dryOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [offline] dry-run の検証に失敗しました");
                }

                var run = await step.ApplyAsync();
                var lines = File.Exists(csvOut) ? File.ReadAllLines(csvOut).Length : 0;
                var runOk = run.Success == 3 && run.Pending == 0 && !run.Stopped && lines == 4;
                _log.Info($"--smoke-etl: [offline] 適用 success={run.Success} / 出力行数（ヘッダー含む）={lines}（期待 4）");
                if (!runOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [offline] 適用の検証に失敗しました");
                }
            }

            // ---- オフライン 2: CSV → SQLite（DB ターゲット + 巻き戻し）----
            var sqlitePath = Path.Combine(tmp, "etl-smoke.db");
            if (File.Exists(sqlitePath))
            {
                File.Delete(sqlitePath);
            }

            var sqliteSpec = new DbConnectionSpec(DbProviderKind.Sqlite, "Data Source=" + sqlitePath);
            await using (var connection = sqliteSpec.CreateConnection())
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE contacts (Name TEXT NOT NULL, Age INTEGER);";
                await command.ExecuteNonQueryAsync();
            }

            var sqliteRunDir = Path.Combine(paths.EtlRunsRoot, "run-smoke-sqlite");
            using (var store = RunStagingStore.Create(sqliteRunDir, "run-smoke-sqlite"))
            {
                async Task<long> CountSqliteAsync()
                {
                    await using var connection = sqliteSpec.CreateConnection();
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM contacts";
                    return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
                }

                var source = new CsvFileSource(csvIn);
                var mapper = new RowMapper(
                    source.Columns,
                    new[]
                    {
                        new FieldMapping("Name", "[Name]"),
                        new FieldMapping("Age", "[Id]", StagingColumnType.Integer),
                    },
                    new ExpressionEngine());
                var dbTarget = new DbTableTarget(sqliteSpec, new DbTableTargetOptions
                {
                    Table = "contacts",
                    Fields = new[] { "Name", "Age" },
                    Op = RowOp.Insert,
                    KeyField = "Name",
                });
                var plan = new EtlStepPlan
                {
                    StepId = "step1",
                    ObjectName = "contacts",
                    Source = source,
                    Mapper = mapper,
                    Target = dbTarget,
                    Revertable = dbTarget,
                };

                var step = new EtlStepRun(store, plan);
                var sqliteResult = await step.RunAsync();
                var sqliteCount = await CountSqliteAsync();
                var sqliteOk = sqliteResult.Apply!.Success == 3 && !sqliteResult.Apply.Stopped && sqliteCount == 3;
                _log.Info($"--smoke-etl: [sqlite] 適用 success={sqliteResult.Apply.Success} / 行数={sqliteCount}（期待 3）");
                if (!sqliteOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [sqlite] 適用の検証に失敗しました");
                }

                var sqliteRollback = await step.RollbackAsync();
                var afterRevert = await CountSqliteAsync();
                var sqliteRevertOk = sqliteRollback.Reverted == 3 && afterRevert == 0;
                _log.Info($"--smoke-etl: [sqlite] 巻き戻し reverted={sqliteRollback.Reverted} / 行数={afterRevert}（期待 0）");
                if (!sqliteRevertOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [sqlite] 巻き戻しの検証に失敗しました");
                }
            }

            // ---- オフライン 3: delta（差分抽出 + watermark 保存/読込み）----
            var deltaCsv = Path.Combine(tmp, "delta.csv");
            File.WriteAllText(
                deltaCsv,
                "Id,ModifiedAt,Name\r\n1,2026-01-01 00:00:00,Alpha\r\n2,2026-01-02 00:00:00,Beta\r\n",
                new UTF8Encoding(false));
            var deltaStatePath = Path.Combine(tmp, "delta-state.json");
            if (File.Exists(deltaStatePath))
            {
                File.Delete(deltaStatePath);
            }

            var fullDelta = new DeltaSource(new CsvFileSource(deltaCsv), "ModifiedAt", null);
            if (fullDelta.MaxValue is { } firstMax)
            {
                DeltaState.SetWatermark(deltaStatePath, "step1", firstMax);
            }

            var savedWatermark = DeltaState.GetWatermark(deltaStatePath, "step1");
            File.AppendAllText(deltaCsv, "3,2026-01-03 00:00:00,Gamma\r\n", new UTF8Encoding(false));
            var incremental = new DeltaSource(new CsvFileSource(deltaCsv), "ModifiedAt", savedWatermark);
            var deltaOk = fullDelta.Count == 2 && savedWatermark == fullDelta.MaxValue && incremental.Count == 1;
            _log.Info($"--smoke-etl: [delta] 全量={fullDelta.Count}（期待 2）/ watermark={savedWatermark:yyyy-MM-dd HH:mm:ss} / 差分={incremental.Count}（期待 1）");
            if (!deltaOk)
            {
                success = false;
                _log.Error("--smoke-etl: [delta] 検証に失敗しました");
            }

            // ---- オフライン 4: retry（失敗行のみ再実行。ステージング済みキューを再利用しソース再読込なし）----
            var retryDbPath = Path.Combine(tmp, "etl-smoke-retry.db");
            if (File.Exists(retryDbPath))
            {
                File.Delete(retryDbPath);
            }

            var retrySpec = new DbConnectionSpec(DbProviderKind.Sqlite, "Data Source=" + retryDbPath);
            await using (var connection = retrySpec.CreateConnection())
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "CREATE TABLE items (Name TEXT PRIMARY KEY, Note TEXT); " +
                    "INSERT INTO items (Name, Note) VALUES ('Alpha', 'x'), ('Beta', 'x');";
                await command.ExecuteNonQueryAsync();
            }

            var retryCsv = Path.Combine(tmp, "retry.csv");
            File.WriteAllText(retryCsv, "Name,Note\r\nAlpha,new\r\nBeta,new\r\nGamma,new\r\n", new UTF8Encoding(false));

            var retryRunDir = Path.Combine(paths.EtlRunsRoot, "run-smoke-retry");
            using (var store = RunStagingStore.Create(retryRunDir, "run-smoke-retry"))
            {
                async Task<long> CountUpdatedAsync()
                {
                    await using var connection = retrySpec.CreateConnection();
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM items WHERE Note = 'new'";
                    return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
                }

                var retrySource = new CsvFileSource(retryCsv);
                var retryMapper = new RowMapper(
                    retrySource.Columns,
                    new[]
                    {
                        new FieldMapping("Name", "[Name]"),
                        new FieldMapping("Note", "[Note]"),
                    },
                    new ExpressionEngine());
                var retryTarget = new DbTableTarget(retrySpec, new DbTableTargetOptions
                {
                    Table = "items",
                    Fields = new[] { "Name", "Note" },
                    Op = RowOp.Update,
                    KeyField = "Name",
                });
                var retryPlan = new EtlStepPlan
                {
                    StepId = "step1",
                    ObjectName = "items",
                    Source = retrySource,
                    Mapper = retryMapper,
                    Target = retryTarget,
                };

                // 初回: Gamma が DB に存在しないため更新失敗（行レベル Failed=1。例外は出ない）
                var retryStep = new EtlStepRun(store, retryPlan);
                var firstRun = await retryStep.RunAsync();
                var firstOk = firstRun.Apply!.Success == 2 && firstRun.Apply.Failed == 1 && !firstRun.Apply.Stopped;
                _log.Info($"--smoke-etl: [retry] 初回 success={firstRun.Apply.Success}（期待 2）/ failed={firstRun.Apply.Failed}（期待 1）");
                if (!firstOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [retry] 初回適用の検証に失敗しました");
                }

                // 失敗原因（Gamma 行の欠落）を解消してから失敗行のみ再実行する（ソースは再読込しない）
                await using (var connection = retrySpec.CreateConnection())
                {
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = "INSERT INTO items (Name, Note) VALUES ('Gamma', 'x');";
                    await command.ExecuteNonQueryAsync();
                }

                var reset = store.ResetFailedToPending("items");
                var retryRunner = new EtlRunner(store, retryTarget, "step1", "items");
                var retryResult = await retryRunner.RunAsync();
                var updated = await CountUpdatedAsync();
                var retryOk = reset == 1 && retryResult.Success == 1 && retryResult.Failed == 0
                    && retryResult.Pending == 0 && updated == 3;
                _log.Info($"--smoke-etl: [retry] 再実行 reset={reset}（期待 1）/ success={retryResult.Success}（期待 1）/ 更新済み={updated}（期待 3）");
                if (!retryOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [retry] 再実行の検証に失敗しました");
                }

                // ---- オフライン 5: verify（移行後の自動検証 = 件数・サンプル値。ステージング済みキューと DB を照合）----
                var verifier = new EtlVerifier(store, retryTarget, "step1", "items", retryMapper.StagingColumns);
                var verifyPass = await verifier.VerifyAsync(CancellationToken.None);
                var verifyPassOk = verifyPass.Passed && verifyPass.OkRows == 3 && verifyPass.FetchedRows == 3
                    && verifyPass.SampledRows == 3 && verifyPass.Mismatches.Count == 0;
                _log.Info($"--smoke-etl: [verify] 件数={verifyPass.OkRows}（期待 3）/ 取得={verifyPass.FetchedRows}（期待 3）/ サンプル={verifyPass.SampledRows}（期待 3）/ 合格={verifyPass.Passed}（期待 True）");
                if (!verifyPassOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [verify] 合格の検証に失敗しました");
                }

                // 1 行改ざん → 不一致を検知する（検証が機能することの確認）
                await using (var connection = retrySpec.CreateConnection())
                {
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = "UPDATE items SET Note = 'tampered' WHERE Name = 'Beta';";
                    await command.ExecuteNonQueryAsync();
                }

                var verifyTampered = await verifier.VerifyAsync(CancellationToken.None);
                var tamperOk = !verifyTampered.Passed && verifyTampered.Mismatches.Count == 1
                    && verifyTampered.Mismatches[0].Field == "Note";
                var mismatchField = verifyTampered.Mismatches.Count > 0 ? verifyTampered.Mismatches[0].Field : "-";
                _log.Info($"--smoke-etl: [verify] 改ざん検知 不一致={verifyTampered.Mismatches.Count}（期待 1）/ 項目={mismatchField}（期待 Note）");
                if (!tamperOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [verify] 改ざん検知の検証に失敗しました");
                }

                // 検証レポート（監査証跡）の保存
                var reportPath = Path.Combine(store.RunDirectory, "verification.json");
                new EtlVerificationReport
                {
                    RunId = store.RunId,
                    VerifiedAt = DateTime.Now.ToString("u"),
                    Steps = new List<EtlVerificationResult> { verifyTampered },
                }.Save(reportPath);
                _log.Info($"--smoke-etl: [verify] レポート={reportPath}（存在={File.Exists(reportPath)}）");
            }

            // ---- オフライン 6: functions（式関数の評価確認。拡充分を含む）----
            {
                var fnEngine = new ExpressionEngine(new ExpressionHost
                {
                    NowProvider = () => new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                    TodayProvider = () => new DateTime(2026, 1, 2),
                    GuidProvider = () => "g",
                });
                var fnChecks = new (string Expression, object? Expected)[]
                {
                    ("SPLIT_PART(\"a|b|c\", \"|\", 2)", "b"),
                    ("LPAD(\"7\", 3, \"0\")", "007"),
                    ("RPAD(\"7\", 3, \"0\")", "700"),
                    ("CONTAINS(\"abcdef\", \"cd\")", true),
                    ("STARTSWITH(\"abcdef\", \"ab\")", true),
                    ("ENDSWITH(\"abcdef\", \"ef\")", true),
                    ("TO_INT(\"12.9\")", 12L),
                    ("IS_NUMBER(\"1,234.5\")", true),
                    ("FORMAT_NUMBER(1234.5, \"0.00\")", "1234.50"),
                    ("ADD_MONTHS(TO_DATE(\"2026-01-31\"), 1)", new DateTime(2026, 2, 28)),
                    ("ADD_HOURS(TO_DATE(\"2026-10-09\"), 13)", new DateTime(2026, 10, 9, 13, 0, 0)),
                    ("DIFF_DAYS(TO_DATE(\"2026-01-10\"), TO_DATE(\"2026-01-01\"))", 9),
                    ("YEAR(TO_DATE(\"2026-10-09\"))", 2026),
                    ("TO_UTC(NOW())", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)),
                    ("NULLIF(\"a\", \"a\")", null),
                    ("IIF(ISBLANK(null), \"x\", \"y\")", "x"),
                    ("ID18(\"003BK00000r7209\")", "003BK00000r7209YAA"),
                    ("ID15(\"003BK00000r7209YAA\")", "003BK00000r7209"),
                };

                var fnOk = 0;
                foreach (var (expression, expected) in fnChecks)
                {
                    try
                    {
                        var value = fnEngine.Compile(expression).EvaluateArgs();
                        if (Equals(value, expected) || (value is null && expected is null))
                        {
                            fnOk++;
                        }
                        else
                        {
                            _log.Error($"--smoke-etl: [functions] 不一致: {expression} = {value}（期待 {expected}）");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"--smoke-etl: [functions] 例外: {expression}: {ex.Message}");
                    }
                }

                _log.Info($"--smoke-etl: [functions] {fnOk}/{fnChecks.Length} 式が一致（期待全件）");
                if (fnOk != fnChecks.Length)
                {
                    success = false;
                    _log.Error("--smoke-etl: [functions] 検証に失敗しました");
                }
            }

            if (string.IsNullOrWhiteSpace(org))
            {
                _log.Info("--smoke-etl: 実組織テストはスキップ（組織未指定。--smoke-etl <org> で実行）");
                return success;
            }

            // ---- 実組織 ----
            var orgs = await Services.GetRequiredService<OrgService>().ListOrgsAsync();
            var orgInfo = orgs.FirstOrDefault(o =>
                string.Equals(o.Alias, org, StringComparison.OrdinalIgnoreCase)
                || string.Equals(o.Username, org, StringComparison.OrdinalIgnoreCase));
            if (orgInfo is null)
            {
                _log.Error($"--smoke-etl: 組織が見つかりません: {org}");
                return false;
            }

            var rest = Services.GetRequiredService<SalesforceRestClient>();
            var apiVersion = await rest.GetApiVersionAsync(org!);
            var cleaned = await DeleteMarkersAsync(rest, org!, apiVersion, "Contact", "LastName", "SfUiEtlE2E%");
            if (cleaned > 0)
            {
                _log.Info($"--smoke-etl: 残骸を掃除 {cleaned} 件");
            }

            async Task<int> CountMarkersAsync(string pattern)
            {
                using var document = await rest.QueryAsync(org!, $"SELECT Id FROM Contact WHERE LastName LIKE '{pattern}'");
                return document.RootElement.TryGetProperty("records", out var records) ? records.GetArrayLength() : 0;
            }

            var marker = "SfUiEtlE2E" + DateTime.Now.ToString("MMddHHmmss", CultureInfo.InvariantCulture);
            var applyOptions = new EtlApplyOptions
            {
                BatchSize = 200,
                MaxErrorRate = 0.25,
                MinRowsForErrorRate = 0,
                MaxConsecutiveFailures = 0,
            };

            // ---- B) 1 行失敗（Email が 80 文字超）→ エラー率停止 → ロールバック ----
            var longEmail = new string('a', 90) + "@example.com";
            var badCsv = Path.Combine(tmp, "bad.csv");
            File.WriteAllText(
                badCsv,
                $"LastName,Email\r\n{marker}_1,ok1@example.com\r\n{marker}_2,ok2@example.com\r\n{marker}_3,{longEmail}\r\n",
                new UTF8Encoding(false));

            var rollbackDir = Path.Combine(paths.EtlRunsRoot, "run-smoke-rollback");
            using (var store = RunStagingStore.Create(rollbackDir, "run-smoke-rollback"))
            {
                var source = new CsvFileSource(badCsv);
                var mapper = new RowMapper(
                    source.Columns,
                    new[] { new FieldMapping("LastName", "[LastName]"), new FieldMapping("Email", "[Email]") },
                    new ExpressionEngine());
                var target = new SalesforceTarget(rest, org!, new SalesforceTargetOptions
                {
                    ObjectName = "Contact",
                    Fields = new[] { "LastName", "Email" },
                    Op = RowOp.Insert,
                }, _log);
                var plan = new EtlStepPlan
                {
                    StepId = "step1",
                    ObjectName = "Contact",
                    Source = source,
                    Mapper = mapper,
                    Target = target,
                    Revertable = target,
                };
                var step = new EtlStepRun(store, plan, applyOptions);
                step.Prepare();
                var apply = await step.ApplyAsync();
                var stopOk = apply.Stopped && apply.StopReason == "error-rate" && apply.Success == 2 && apply.Failed == 1;
                _log.Info($"--smoke-etl: [org] 失敗行あり適用 success={apply.Success} / failed={apply.Failed} / stop={apply.StopReason}");
                if (!stopOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [org] エラー率停止の検証に失敗しました");
                }

                var rollback = await step.RollbackAsync();   // RunAsync の自動ロールバックと同じ処理
                var remaining = await CountMarkersAsync(marker + "%");
                var rollbackOk = rollback.Reverted == 2 && rollback.Failed == 0 && remaining == 0;
                _log.Info($"--smoke-etl: [org] ロールバック reverted={rollback.Reverted} / failed={rollback.Failed} / 残件={remaining}（期待 0）");
                if (!rollbackOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [org] ロールバックの検証に失敗しました");
                }
            }

            // ---- C) 全行成功 → 成功後巻き戻し（手動リストア）----
            var goodCsv = Path.Combine(tmp, "good.csv");
            File.WriteAllText(
                goodCsv,
                $"LastName,Email\r\n{marker}_1,ok1@example.com\r\n{marker}_2,ok2@example.com\r\n{marker}_3,ok3@example.com\r\n",
                new UTF8Encoding(false));

            var successDir = Path.Combine(paths.EtlRunsRoot, "run-smoke-success");
            using (var store = RunStagingStore.Create(successDir, "run-smoke-success"))
            {
                var source = new CsvFileSource(goodCsv);
                var mapper = new RowMapper(
                    source.Columns,
                    new[] { new FieldMapping("LastName", "[LastName]"), new FieldMapping("Email", "[Email]") },
                    new ExpressionEngine());
                var target = new SalesforceTarget(rest, org!, new SalesforceTargetOptions
                {
                    ObjectName = "Contact",
                    Fields = new[] { "LastName", "Email" },
                    Op = RowOp.Insert,
                }, _log);
                var plan = new EtlStepPlan
                {
                    StepId = "step1",
                    ObjectName = "Contact",
                    Source = source,
                    Mapper = mapper,
                    Target = target,
                    Revertable = target,
                };
                var step = new EtlStepRun(store, plan, applyOptions);
                step.Prepare();
                var apply = await step.ApplyAsync();
                var appliedCount = await CountMarkersAsync(marker + "%");
                var applyOk = !apply.Stopped && apply.Success == 3 && appliedCount == 3;
                _log.Info($"--smoke-etl: [org] 全行適用 success={apply.Success} / 組織内マーカー={appliedCount}（期待 3）");
                if (!applyOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [org] 適用の検証に失敗しました");
                }

                // 移行後の自動検証（実レコードを SOQL で取得して件数・サンプル値を照合）
                var verifier = new EtlVerifier(store, target, "step1", "Contact", mapper.StagingColumns);
                var verifyResult = await verifier.VerifyAsync(CancellationToken.None);
                var verifyOk = verifyResult.Passed && verifyResult.OkRows == 3 && verifyResult.FetchedRows == 3
                    && verifyResult.MissingKeys.Count == 0 && verifyResult.Mismatches.Count == 0;
                _log.Info($"--smoke-etl: [verify-org] 合格={verifyResult.Passed} / OK={verifyResult.OkRows} / 取得={verifyResult.FetchedRows} / 欠落={verifyResult.MissingKeys.Count} / サンプル={verifyResult.SampledRows} / 不一致={verifyResult.Mismatches.Count}");
                if (!verifyOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [verify-org] 組織検証に失敗しました");
                }

                var rollback = await step.RollbackAsync();
                var remaining = await CountMarkersAsync(marker + "%");
                var rollbackOk = rollback.Reverted == 3 && remaining == 0;
                _log.Info($"--smoke-etl: [org] 成功後巻き戻し reverted={rollback.Reverted} / 残件={remaining}（期待 0）");
                if (!rollbackOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [org] 巻き戻しの検証に失敗しました");
                }
            }

            // ---- D) マルチステップ: Account（親）→ Contact（子）を LOOKUP で接続 ----
            var multiMarker = "SfUiEtlJob" + DateTime.Now.ToString("MMddHHmmss", CultureInfo.InvariantCulture);
            var parentCsv = Path.Combine(tmp, "parents.csv");
            File.WriteAllText(parentCsv, $"Name\r\n{multiMarker}_A\r\n{multiMarker}_B\r\n", new UTF8Encoding(false));
            var childCsv = Path.Combine(tmp, "children.csv");
            File.WriteAllText(
                childCsv,
                $"LastName,ParentName\r\n{multiMarker}_C1,{multiMarker}_A\r\n{multiMarker}_C2,{multiMarker}_B\r\n",
                new UTF8Encoding(false));

            var multiDir = Path.Combine(paths.EtlRunsRoot, "run-smoke-multistep");
            using (var store = RunStagingStore.Create(multiDir, "run-smoke-multistep"))
            {
                var host = new ExpressionHost
                {
                    UserName = Environment.UserName,
                    OrgName = org!,
                    MachineName = Environment.MachineName,
                };
                EtlCrosswalk.WireLookup(host, store);

                var parentSource = new CsvFileSource(parentCsv);
                var parentMapper = new RowMapper(
                    parentSource.Columns,
                    new[] { new FieldMapping("Name", "[Name]") },
                    new ExpressionEngine(host));
                var parentTarget = new SalesforceTarget(rest, org!, new SalesforceTargetOptions
                {
                    ObjectName = "Account",
                    Fields = new[] { "Name" },
                    Op = RowOp.Insert,
                }, _log);
                var parentStep = new EtlStepPlan
                {
                    StepId = "step1",
                    ObjectName = "Account",
                    Source = parentSource,
                    Mapper = parentMapper,
                    Target = parentTarget,
                    Revertable = parentTarget,
                    CrosswalkKeyField = "Name",
                };

                var childSource = new CsvFileSource(childCsv);
                var childMapper = new RowMapper(
                    childSource.Columns,
                    new[]
                    {
                        new FieldMapping("LastName", "[LastName]"),
                        new FieldMapping("AccountId", "LOOKUP(\"Account\", \"Name\", [ParentName])"),
                    },
                    new ExpressionEngine(host));
                var childTarget = new SalesforceTarget(rest, org!, new SalesforceTargetOptions
                {
                    ObjectName = "Contact",
                    Fields = new[] { "LastName", "AccountId" },
                    Op = RowOp.Insert,
                }, _log);
                var childStep = new EtlStepPlan
                {
                    StepId = "step2",
                    ObjectName = "Contact",
                    Source = childSource,
                    Mapper = childMapper,
                    Target = childTarget,
                    Revertable = childTarget,
                };

                var job = new EtlJobRunner(store, new EtlJobPlan
                {
                    JobName = "smoke-multistep",
                    Steps = new[] { parentStep, childStep },
                });
                var jobResult = await job.RunAsync();

                var stepOk = jobResult.Steps.Count == 2
                    && jobResult.Steps[0].Apply!.Success == 2
                    && jobResult.Steps[1].Apply!.Success == 2
                    && !jobResult.Stopped;
                _log.Info($"--smoke-etl: [multistep] 親成功={jobResult.Steps.ElementAtOrDefault(0)?.Apply?.Success} / 子成功={jobResult.Steps.ElementAtOrDefault(1)?.Apply?.Success}（期待 2 / 2）");
                if (!stepOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [multistep] 適用の検証に失敗しました");
                }

                // 子 Contact が LOOKUP で解決した親 Account を指しているか
                var linkedOk = false;
                using (var document = await rest.QueryAsync(org!, $"SELECT Id, AccountId, Account.Name FROM Contact WHERE LastName LIKE '{multiMarker}%'"))
                {
                    var records = document.RootElement.GetProperty("records");
                    linkedOk = records.GetArrayLength() == 2;
                    if (linkedOk)
                    {
                        foreach (var record in records.EnumerateArray())
                        {
                            var accountId = record.TryGetProperty("AccountId", out var accId) && accId.ValueKind != JsonValueKind.Null ? accId.GetString() : null;
                            var accountName = record.TryGetProperty("Account", out var acc) && acc.TryGetProperty("Name", out var name)
                                ? name.GetString()
                                : null;
                            if (string.IsNullOrEmpty(accountId) ||
                                accountName is null ||
                                !accountName.StartsWith(multiMarker, StringComparison.Ordinal))
                            {
                                linkedOk = false;
                                break;
                            }
                        }
                    }
                }

                _log.Info($"--smoke-etl: [multistep] 子→親リンク OK={linkedOk}");
                if (!linkedOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [multistep] LOOKUP による Id 受渡しの検証に失敗しました");
                }

                // ---- E) Salesforce 入力: SOQL → CSV（REST ページングで親 Account を読み戻す）----
                var soqlOut = Path.Combine(tmp, "soql-out.csv");
                if (File.Exists(soqlOut))
                {
                    File.Delete(soqlOut);
                }

                var soqlSource = new SalesforceSource(
                    rest, org!, $"SELECT Id, Name FROM Account WHERE Name LIKE '{multiMarker}%' ORDER BY Name");
                var soqlMapper = new RowMapper(
                    soqlSource.Columns,
                    new[] { new FieldMapping("Id", "[Id]"), new FieldMapping("Name", "[Name]") },
                    new ExpressionEngine(host));
                var soqlPlan = new EtlStepPlan
                {
                    StepId = "step3",
                    ObjectName = "AccountSoql",
                    Source = soqlSource,
                    Mapper = soqlMapper,
                    Target = new CsvFileTarget(soqlOut, new[] { "Id", "Name" }),
                };
                var soqlResult = await new EtlStepRun(store, soqlPlan).RunAsync();
                var soqlLines = File.Exists(soqlOut) ? File.ReadAllLines(soqlOut).Length : 0;
                var soqlOk = soqlSource.Columns.Count == 2
                    && soqlResult.Apply!.Success == 2
                    && !soqlResult.Apply.Stopped
                    && soqlLines == 3;
                _log.Info($"--smoke-etl: [soql] 列数={soqlSource.Columns.Count} / 適用 success={soqlResult.Apply!.Success} / 出力行数={soqlLines}（期待 2 / 2 / 3）");
                if (!soqlOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [soql] 検証に失敗しました");
                }

                // ---- F) Bulk 入力: sf data export bulk → CSV（大量件数向けの経路）----
                var bulkOut = Path.Combine(tmp, "bulk-out.csv");
                if (File.Exists(bulkOut))
                {
                    File.Delete(bulkOut);
                }

                var bulkSource = new SalesforceBulkSource(
                    Services.GetRequiredService<SfCliRunner>(),
                    org!,
                    $"SELECT Id, Name FROM Account WHERE Name LIKE '{multiMarker}%'",
                    Path.Combine(tmp, "bulk-work"));
                var bulkMapper = new RowMapper(
                    bulkSource.Columns,
                    new[] { new FieldMapping("Id", "[Id]"), new FieldMapping("Name", "[Name]") },
                    new ExpressionEngine(host));
                var bulkPlan = new EtlStepPlan
                {
                    StepId = "step4",
                    ObjectName = "AccountBulk",
                    Source = bulkSource,
                    Mapper = bulkMapper,
                    Target = new CsvFileTarget(bulkOut, new[] { "Id", "Name" }),
                };
                var bulkResult = await new EtlStepRun(store, bulkPlan).RunAsync();
                var bulkLines = File.Exists(bulkOut) ? File.ReadAllLines(bulkOut).Length : 0;
                var bulkOk = bulkSource.Columns.Count == 2
                    && bulkResult.Apply!.Success == 2
                    && !bulkResult.Apply.Stopped
                    && bulkLines == 3;
                _log.Info($"--smoke-etl: [bulk] 列数={bulkSource.Columns.Count} / 適用 success={bulkResult.Apply!.Success} / 出力行数={bulkLines}（期待 2 / 2 / 3）");
                if (!bulkOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [bulk] 検証に失敗しました");
                }

                // 子 → 親の順に巻き戻し（成功後の巻き戻し）
                var childRollback = await new EtlStepRun(store, childStep).RollbackAsync();
                var parentRollback = await new EtlStepRun(store, parentStep).RollbackAsync();
                var remainingContacts = await DeleteMarkersAsync(rest, org!, apiVersion, "Contact", "LastName", multiMarker + "%");
                var remainingAccounts = await DeleteMarkersAsync(rest, org!, apiVersion, "Account", "Name", multiMarker + "%");
                var revertOk = childRollback.Reverted == 2 && parentRollback.Reverted == 2 && remainingContacts == 0 && remainingAccounts == 0;
                _log.Info($"--smoke-etl: [multistep] 巻き戻し 子={childRollback.Reverted} / 親={parentRollback.Reverted} / 残件 Contact={remainingContacts} Account={remainingAccounts}（期待 2 / 2 / 0 / 0）");
                if (!revertOk)
                {
                    success = false;
                    _log.Error("--smoke-etl: [multistep] 巻き戻しの検証に失敗しました");
                }
            }

            // ---- G) SOQL delta push-down: 差分条件を SOQL へ押し込み、変更分のみ取得 ----
            {
                var dpMarker = "SfUiEtlDp" + DateTime.Now.ToString("MMddHHmmss", CultureInfo.InvariantCulture);
                await CreateSmokeRecordAsync(rest, org!, apiVersion, "Contact", new Dictionary<string, object?>
                {
                    ["LastName"] = dpMarker + "_A",
                });

                var dpSoql = $"SELECT Id, LastName, LastModifiedDate FROM Contact WHERE LastName LIKE '{dpMarker}%' ORDER BY LastName";
                var dpStatePath = Path.Combine(tmp, "dp-state.json");
                if (File.Exists(dpStatePath))
                {
                    File.Delete(dpStatePath);
                }

                try
                {
                    // 1) 初回: watermark なし → プッシュダウンなし（全件）
                    var wm0 = DeltaState.GetWatermark(dpStatePath, "step1");
                    var noPush = !SoqlDeltaPushDown.TryBuild(dpSoql, "LastModifiedDate", wm0, out _);
                    var first = new DeltaSource(new SalesforceSource(rest, org!, dpSoql), "LastModifiedDate", wm0);
                    if (first.MaxValue is { } max1)
                    {
                        DeltaState.SetWatermark(dpStatePath, "step1", max1);
                    }

                    // 2) 1 件追加 → 2 回目は押し込みで変更分だけ取得できる
                    //    注意: SF の LastModifiedDate は秒精度 → A と B を同じ秒に入れると差分 0 件になるため待つ
                    await Task.Delay(1100);
                    await CreateSmokeRecordAsync(rest, org!, apiVersion, "Contact", new Dictionary<string, object?>
                    {
                        ["LastName"] = dpMarker + "_B",
                    });
                    var wm1 = DeltaState.GetWatermark(dpStatePath, "step1");
                    var pushed = SoqlDeltaPushDown.TryBuild(dpSoql, "LastModifiedDate", wm1, out var pushedSoql);
                    var second = new DeltaSource(new SalesforceSource(rest, org!, pushedSoql), "LastModifiedDate", wm1);
                    var dpOk = noPush && first.Count == 1 && pushed && second.Count == 1
                        && pushedSoql.Contains("(LastName LIKE", StringComparison.Ordinal)
                        && pushedSoql.Contains("LastModifiedDate >", StringComparison.Ordinal)
                        && pushedSoql.EndsWith("ORDER BY LastName", StringComparison.OrdinalIgnoreCase);
                    _log.Info($"--smoke-etl: [soql-delta] 初回={first.Count}（期待 1）/ 押し込み={pushed}（期待 True）/ 差分={second.Count}（期待 1）");
                    if (!dpOk)
                    {
                        success = false;
                        _log.Error("--smoke-etl: [soql-delta] 検証に失敗しました");
                    }
                }
                finally
                {
                    var dpCleaned = await DeleteMarkersAsync(rest, org!, apiVersion, "Contact", "LastName", dpMarker + "%");
                    _log.Info($"--smoke-etl: [soql-delta] クリーンアップ {dpCleaned} 件（期待 2）");
                }
            }

            // ---- H) 非 CSV 入力（Excel / JSON / XML / DB）→ Salesforce 実組織 ----
            var inMarkerBase = "SfUiEtlIn" + DateTime.Now.ToString("MMddHHmmss", CultureInfo.InvariantCulture);

            var excelPath = Path.Combine(tmp, "inputs.xlsx");
            var excelMarker = inMarkerBase + "Xl";
            ExcelExporter.Write(excelPath, new[]
            {
                new ExportSheet
                {
                    Name = "Contacts",
                    Columns = new[] { "LastName", "Email" },
                    Rows = new IReadOnlyList<string?>[]
                    {
                        new[] { excelMarker + "_1", "xl1@example.com" },
                        new[] { excelMarker + "_2", "xl2@example.com" },
                    },
                },
            });

            var jsonPath = Path.Combine(tmp, "inputs.json");
            var jsonMarker = inMarkerBase + "Js";
            File.WriteAllText(
                jsonPath,
                "[" +
                $"{{\"LastName\":\"{jsonMarker}_1\",\"Email\":\"js1@example.com\"}}," +
                $"{{\"LastName\":\"{jsonMarker}_2\",\"Email\":\"js2@example.com\"}}" +
                "]",
                new UTF8Encoding(false));

            var xmlPath = Path.Combine(tmp, "inputs.xml");
            var xmlMarker = inMarkerBase + "Xm";
            File.WriteAllText(
                xmlPath,
                $"<rows><row><LastName>{xmlMarker}_1</LastName><Email>xm1@example.com</Email></row>" +
                $"<row><LastName>{xmlMarker}_2</LastName><Email>xm2@example.com</Email></row></rows>",
                new UTF8Encoding(false));

            var inputsDbPath = Path.Combine(tmp, "inputs.db");
            if (File.Exists(inputsDbPath))
            {
                File.Delete(inputsDbPath);
            }

            var inputsDbSpec = new DbConnectionSpec(DbProviderKind.Sqlite, "Data Source=" + inputsDbPath);
            var dbMarker = inMarkerBase + "Db";
            await using (var connection = inputsDbSpec.CreateConnection())
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "CREATE TABLE src (LastName TEXT, Email TEXT); " +
                    $"INSERT INTO src (LastName, Email) VALUES ('{dbMarker}_1', 'db1@example.com'), ('{dbMarker}_2', 'db2@example.com');";
                await command.ExecuteNonQueryAsync();
            }

            var inputChecks = new (string Name, Func<IEtlSource> Create, string Marker)[]
            {
                ("Excel", () => new ExcelFileSource(excelPath, null, true), excelMarker),
                ("JSON", () => new JsonFileSource(jsonPath), jsonMarker),
                ("XML", () => new XmlFileSource(xmlPath, "row"), xmlMarker),
                ("DB", () => new DbTableSource(inputsDbSpec, "SELECT LastName, Email FROM src ORDER BY LastName"), dbMarker),
            };

            foreach (var (inputName, create, inputMarker) in inputChecks)
            {
                var inputDir = Path.Combine(paths.EtlRunsRoot, "run-smoke-input-" + inputName.ToLowerInvariant());
                using (var store = RunStagingStore.Create(inputDir, "run-smoke-input-" + inputName.ToLowerInvariant()))
                {
                    var inputSource = create();
                    var inputMapper = new RowMapper(
                        inputSource.Columns,
                        new[] { new FieldMapping("LastName", "[LastName]"), new FieldMapping("Email", "[Email]") },
                        new ExpressionEngine());
                    var inputTarget = new SalesforceTarget(rest, org!, new SalesforceTargetOptions
                    {
                        ObjectName = "Contact",
                        Fields = new[] { "LastName", "Email" },
                        Op = RowOp.Insert,
                    }, _log);
                    var inputPlan = new EtlStepPlan
                    {
                        StepId = "step1",
                        ObjectName = "Contact",
                        Source = inputSource,
                        Mapper = inputMapper,
                        Target = inputTarget,
                        Revertable = inputTarget,
                    };
                    var inputStep = new EtlStepRun(store, inputPlan, applyOptions);
                    inputStep.Prepare();
                    var inputApply = await inputStep.ApplyAsync();
                    var inserted = await CountMarkersAsync(inputMarker + "%");
                    var inputRollback = await inputStep.RollbackAsync();
                    var afterRevert = await CountMarkersAsync(inputMarker + "%");
                    var inputOk = !inputApply.Stopped && inputApply.Success == 2 && inserted == 2
                        && inputRollback.Reverted == 2 && afterRevert == 0;
                    _log.Info($"--smoke-etl: [inputs] {inputName} 適用={inputApply.Success}（期待 2）/ 投入後={inserted}（期待 2）/ 巻き戻し={inputRollback.Reverted}（期待 2）/ 残件={afterRevert}（期待 0）");
                    if (!inputOk)
                    {
                        success = false;
                        _log.Error($"--smoke-etl: [inputs] {inputName} の検証に失敗しました");
                    }
                }

                // 保険（巻き戻し失敗時も残さない）
                await DeleteMarkersAsync(rest, org!, apiVersion, "Contact", "LastName", inputMarker + "%");
            }

            // 後片付け（保険）
            var leftovers = await DeleteMarkersAsync(rest, org!, apiVersion, "Contact", "LastName", marker + "%");
            if (leftovers > 0)
            {
                _log.Info($"--smoke-etl: 後片付け {leftovers} 件を削除");
            }

            _log.Info(success ? "--smoke-etl: すべての検証に成功しました" : "--smoke-etl: 一部の検証に失敗しました");
        }
        catch (Exception ex)
        {
            _log.Error("--smoke-etl: 検証に失敗しました", ex);
            return false;
        }

        return success;
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Length ? args[i + 1] : null;
            }

            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(name.Length + 1)..];
            }
        }

        return null;
    }
}
