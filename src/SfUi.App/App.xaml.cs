using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.ViewModels;
using SfUi.Core;

namespace SfUi.App;

public partial class App : Application
{
    /// <summary>DI コンテナ（後続フェーズの各ビューから利用する）</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    private AppLog _log = null!;
    private bool _smokeTest;
    private bool _smokeAi;
    private string? _smokeOrg;
    private int _dispatcherExceptionCount;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _smokeTest = e.Args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase));
        _smokeAi = _smokeTest && e.Args.Any(a => string.Equals(a, "--smoke-ai", StringComparison.OrdinalIgnoreCase));
        _smokeOrg = _smokeTest ? ReadOption(e.Args, "--smoke-org") : null;
        var dataDir = ReadOption(e.Args, "--data-dir") ?? Environment.GetEnvironmentVariable("SFUI_DATA_DIR");

        var paths = AppPaths.Resolve(dataDir);

        var services = new ServiceCollection();
        services.AddSfUiCore(paths);
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SoqlViewModel>();
        services.AddSingleton<ApexViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<CommandViewModel>();
        services.AddSingleton<ApiConsoleViewModel>();
        services.AddSingleton<DeployViewModel>();
        services.AddSingleton<AiChatViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<QuickPanelViewModel>();
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
            for (var tabIndex = 0; tabIndex < 9; tabIndex++)
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
            _log.Info($"--smoke: 言語切替OK (ja→en, 例外 {_dispatcherExceptionCount} 件)");

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
                var aiClient = Services.GetRequiredService<DeepSeekClient>();
                _log.Info($"--smoke-ai: model={aiClient.Model} / apiKey={(aiClient.ApiKey is null ? "(未設定)" : "(設定済み)")}");
                var aiResult = await aiClient.ChatAsync(new[] { new DeepSeekClient.ChatMessage("user", "Reply with the single word: OK") });
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
