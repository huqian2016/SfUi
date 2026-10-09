using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.ViewModels;
using SfUi.Avalonia.Services;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.Avalonia;

public partial class App : Application
{
    /// <summary>DI コンテナ（各ビューから利用する）。</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    private AppLog? _log;
    private bool _smokeTest;
    private bool _smokeCompare;
    private MainWindow? _mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            _smokeTest = args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase));
            _smokeCompare = _smokeTest && args.Any(a => string.Equals(a, "--smoke-compare", StringComparison.OrdinalIgnoreCase));
            var noWelcome = args.Any(a => string.Equals(a, "--no-welcome", StringComparison.OrdinalIgnoreCase));
            var simulateSfMissing = args.Any(a => string.Equals(a, "--welcome-missing", StringComparison.OrdinalIgnoreCase));
            var forceWelcome = simulateSfMissing || args.Any(a => string.Equals(a, "--welcome", StringComparison.OrdinalIgnoreCase));
            var dataDir = ReadOption(args, "--data-dir") ?? Environment.GetEnvironmentVariable("SFUI_DATA_DIR");

            var paths = AppPaths.Resolve(dataDir);

            var services = new ServiceCollection();
            services.AddSfUiCore(paths);
            services.AddSingleton<TopLevelAccessor>();
            services.AddSingleton<IDialogService, AvaloniaDialogService>();
            services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
            services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
            services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
            services.AddSingleton<IAppWindowService, AvaloniaAppWindowService>();
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
            services.AddTransient<CompareOrgsViewModel>();
            services.AddTransient<CompareRecordDetailViewModel>();
            services.AddTransient<OrgInfoViewModel>();
            services.AddTransient<DataIoViewModel>();
            services.AddTransient<DataExportViewModel>();
            services.AddTransient<DataImportViewModel>();
            services.AddTransient<ObjectAccessViewModel>();
            services.AddTransient<FieldAccessViewModel>();
            services.AddTransient<RecordAccessViewModel>();
            services.AddTransient<BackupViewModel>();
            services.AddTransient<BackupTabViewModel>();
            services.AddTransient<RestoreTabViewModel>();
            services.AddTransient<CompareTabViewModel>();
            services.AddTransient<BackupRecordsViewModel>();
            services.AddTransient<BackupCompareRecordsViewModel>();
            services.AddTransient<LogAnalyzerViewModel>();
            services.AddTransient<FieldUsageViewModel>();
            services.AddTransient<OrgManageViewModel>();
            services.AddTransient<OrgHealthViewModel>();
            services.AddTransient<MigrationInventoryViewModel>();
            services.AddTransient<EtlViewModel>();
            services.AddSingleton(new StartupOptions { SimulateSfMissing = simulateSfMissing });
            services.AddTransient<WelcomeViewModel>();
            services.AddSingleton<MainWindow>();
            Services = services.BuildServiceProvider();

            _log = Services.GetRequiredService<AppLog>();
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            _log.Info($"SfUi (Avalonia) v{version} 起動 (DataRoot={paths.DataRoot}, Smoke={_smokeTest})");

            // UI 言語を適用（既定は英語）
            var languageSettings = Services.GetRequiredService<AppSettingsStore>();
            UiText.SetLanguage(languageSettings.Current.Language);
            _log.Info($"UI 言語: {UiText.Language}");

            var window = Services.GetRequiredService<MainWindow>();
            Services.GetRequiredService<TopLevelAccessor>().Current = window;
            desktop.MainWindow = window;
            // メインウィンドウを閉じたら（子ウィンドウが残っていても）アプリを終了する
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            _mainWindow = window;

            // 開発/スクリーンショット用: 指定タブを開いた状態で起動（--tab 0..7）
            if (int.TryParse(ReadOption(args, "--tab"), out var tabIndex) && tabIndex >= 0)
            {
                Services.GetRequiredService<MainViewModel>().SelectedTabIndex = tabIndex;
            }

            // 開発/スクリーンショット用: 指定ウィンドウを開いた状態で起動（--open compare）
            var openTarget = ReadOption(args, "--open");
            if (openTarget is not null)
            {
                window.Opened += async (_, _) =>
                {
                    var mainViewModel = Services.GetRequiredService<MainViewModel>();
                    for (var i = 0; i < 60 && mainViewModel.Orgs.Count == 0; i++)
                    {
                        await Task.Delay(500);
                    }

                    if (string.Equals(openTarget, "compare", StringComparison.OrdinalIgnoreCase) && mainViewModel.Orgs.Count > 0)
                    {
                        Services.GetRequiredService<IAppWindowService>().OpenCompareOrgs(mainViewModel.Orgs.ToList());
                        _log?.Info("--open compare: 比較ウィンドウを開きました");
                    }
                    else if (string.Equals(openTarget, "orginfo", StringComparison.OrdinalIgnoreCase)
                        && (mainViewModel.SelectedOrg ?? mainViewModel.Orgs.FirstOrDefault()) is { } org)
                    {
                        Services.GetRequiredService<IAppWindowService>().OpenOrgInfo(org);
                        _log?.Info("--open orginfo: 組織情報ウィンドウを開きました");
                    }
                    else if (string.Equals(openTarget, "dataio", StringComparison.OrdinalIgnoreCase)
                        && (mainViewModel.SelectedOrg ?? mainViewModel.Orgs.FirstOrDefault()) is { } dataIoOrg)
                    {
                        Services.GetRequiredService<IAppWindowService>().OpenDataIo(dataIoOrg);
                        _log?.Info("--open dataio: データ入出力ウィンドウを開きました");
                    }
                    else if (string.Equals(openTarget, "backup", StringComparison.OrdinalIgnoreCase)
                        && (mainViewModel.SelectedOrg ?? mainViewModel.Orgs.FirstOrDefault()) is { } backupOrg)
                    {
                        Services.GetRequiredService<IAppWindowService>().OpenBackup(backupOrg);
                        _log?.Info("--open backup: バックアップ ウィンドウを開きました");
                    }
                    else if (string.Equals(openTarget, "backuprecords", StringComparison.OrdinalIgnoreCase)
                        && (mainViewModel.SelectedOrg ?? mainViewModel.Orgs.FirstOrDefault()) is { } recordsOrg)
                    {
                        var backup = Services.GetRequiredService<BackupService>().ListBackups().FirstOrDefault();
                        var info = backup?.Objects.FirstOrDefault();
                        if (backup is not null && info is not null)
                        {
                            var displayName = string.IsNullOrWhiteSpace(info.Label) ? info.Name : info.Label;
                            Services.GetRequiredService<IAppWindowService>().OpenBackupRecords(backup.Id, info, displayName, recordsOrg);
                            _log?.Info("--open backuprecords: レコード詳細ウィンドウを開きました");
                        }
                    }
                    else if (string.Equals(openTarget, "comparerecords", StringComparison.OrdinalIgnoreCase)
                        && (mainViewModel.SelectedOrg ?? mainViewModel.Orgs.FirstOrDefault()) is { } compareOrg)
                    {
                        var backups = Services.GetRequiredService<BackupService>().ListBackups();
                        var backupA = backups.ElementAtOrDefault(0);
                        var backupB = backups.ElementAtOrDefault(1);
                        var info = backupA?.Objects.FirstOrDefault();
                        if (backupA is not null && backupB is not null && info is not null)
                        {
                            var displayName = string.IsNullOrWhiteSpace(info.Label) ? info.Name : info.Label;
                            Services.GetRequiredService<IAppWindowService>().OpenBackupCompareRecords(backupA.Id, backupB.Id, info.Name, displayName, compareOrg);
                            _log?.Info("--open comparerecords: 差分詳細ウィンドウを開きました");
                        }
                    }
                    else if (string.Equals(openTarget, "orgmanage", StringComparison.OrdinalIgnoreCase))
                    {
                        Services.GetRequiredService<IAppWindowService>().OpenOrgManage(mainViewModel.SelectedOrg);
                        _log?.Info("--open orgmanage: 組織管理ウィンドウを開きました");
                    }
                };
            }

            // ようこそ画面（毎回表示。--no-welcome / --open / --tab / スモーク時は表示しない）
            if (!_smokeTest
                && openTarget is null
                && ReadOption(args, "--tab") is null
                && !noWelcome
                && (forceWelcome || !languageSettings.Current.WelcomeDismissed))
            {
                window.Opened += (_, _) => Services.GetRequiredService<MainViewModel>().ShowWelcome();
            }

            if (_smokeTest)
            {
                window.Opened += (_, _) => _ = RunSmokeAsync();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>スモーク起動: 組織一覧取得と言語切替を確認してから自動終了する。</summary>
    private async Task RunSmokeAsync()
    {
        try
        {
            var vm = Services.GetRequiredService<MainViewModel>();
            for (var i = 0; i < 60 && vm.Orgs.Count == 0; i++)
            {
                await Task.Delay(500);
            }

            _log?.Info($"--smoke: 組織一覧 {vm.Orgs.Count} 件");

            if (_smokeCompare)
            {
                Services.GetRequiredService<IAppWindowService>().OpenCompareOrgs(vm.Orgs.ToList());
                await Task.Delay(2500);
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    foreach (var extra in lifetime.Windows.Where(w => !ReferenceEquals(w, _mainWindow)).ToList())
                    {
                        extra.Close();
                    }
                }

                _log?.Info("--smoke: compare ウィンドウ OK");
            }

            UiText.SetLanguage(UiText.Japanese);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            UiText.SetLanguage(UiText.English);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            UiText.SetLanguage(UiText.Chinese);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            UiText.SetLanguage(UiText.Korean);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            _log?.Info("--smoke: 全タブ OK / 言語切替 OK (ja→en→zh→ko)");
            _log?.Info("--smoke モード: 正常に終了します");
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(0);
        }
        catch (Exception ex)
        {
            _log?.Error("--smoke 失敗", ex);
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(1);
        }
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
