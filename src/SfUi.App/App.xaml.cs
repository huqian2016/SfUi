using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SfUi.App.Services;
using SfUi.App.ViewModels;
using SfUi.App.Views;
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
    private string? _smokeOrgInfo;
    private bool _smokeOrgInfoRefresh;
    private string? _smokeCompare;
    private string? _smokeDataIo;
    private string? _smokeAccess;
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
        _smokeDataIo = _smokeTest ? ReadOption(e.Args, "--smoke-dataio") : null;
        _smokeAccess = _smokeTest ? ReadOption(e.Args, "--smoke-access") : null;
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
        services.AddTransient<AiChatViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<QuickPanelViewModel>();
        services.AddSingleton<OrgInfoWindowFactory>();
        services.AddTransient<OrgInfoViewModel>();
        services.AddTransient<OrgInfoWindow>();
        services.AddSingleton<CompareOrgsWindowFactory>();
        services.AddTransient<CompareOrgsViewModel>();
        services.AddTransient<CompareOrgsWindow>();
        services.AddSingleton<DataIoWindowFactory>();
        services.AddTransient<DataIoViewModel>();
        services.AddTransient<DataExportViewModel>();
        services.AddTransient<DataImportViewModel>();
        services.AddTransient<ObjectAccessViewModel>();
        services.AddTransient<FieldAccessViewModel>();
        services.AddTransient<RecordAccessViewModel>();
        services.AddTransient<DataIoWindow>();
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
