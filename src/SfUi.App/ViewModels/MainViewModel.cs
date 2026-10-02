using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SfUi.App.Views;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>メインウィンドウ（上部バー・ステータスバー）の ViewModel。</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly OrgService _orgService;
    private readonly AppLog _log;
    private readonly AppSettingsStore _settings;
    private readonly RecentFoldersStore _recentFolders;
    private readonly RecentUrlsStore _recentUrls;
    private readonly ToolLauncherService _toolLauncher;
    private readonly HistoryStore _history;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "準備完了";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDetail))]
    private OrgInfo? _selectedOrg;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDetail))]
    private string? _selectedFolder;

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>クイックパネル（左サイド）の表示状態。</summary>
    [ObservableProperty]
    private bool _isQuickPanelVisible = true;

    /// <summary>ステータスバー表示用（組織・フォルダ）。</summary>
    public string StatusDetail => $"組織: {SelectedOrg?.DisplayName ?? "未選択"} ／ フォルダ: {SelectedFolder ?? "（既定）"}";

    /// <summary>認証済み組織（既定組織が先頭）。</summary>
    public ObservableCollection<OrgInfo> Orgs { get; } = new();

    /// <summary>最近使用した SF 実行フォルダ（ピン留め優先）。</summary>
    public ObservableCollection<RecentFolder> RecentFolders { get; } = new();

    /// <summary>最近開いた URL（ブラウザメニュー用）。</summary>
    public ObservableCollection<RecentUrl> RecentUrls { get; } = new();

    /// <summary>履歴タブの ViewModel。</summary>
    public HistoryViewModel History { get; }

    /// <summary>SOQL タブの ViewModel。</summary>
    public SoqlViewModel Soql { get; }

    /// <summary>匿名Apex タブの ViewModel。</summary>
    public ApexViewModel Apex { get; }

    /// <summary>デバッグログタブの ViewModel。</summary>
    public LogViewModel Logs { get; }

    /// <summary>sf 自由コマンドタブの ViewModel。</summary>
    public CommandViewModel Command { get; }

    /// <summary>REST コンソールタブの ViewModel。</summary>
    public ApiConsoleViewModel Api { get; }

    /// <summary>デプロイタブの ViewModel。</summary>
    public DeployViewModel Deploy { get; }

    /// <summary>設定タブの ViewModel。</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>クイックパネル（お気に入り）の ViewModel。</summary>
    public QuickPanelViewModel QuickPanel { get; }

    public MainViewModel(
        OrgService orgService,
        AppLog log,
        AppSettingsStore settings,
        RecentFoldersStore recentFolders,
        RecentUrlsStore recentUrls,
        ToolLauncherService toolLauncher,
        HistoryStore historyStore,
        HistoryViewModel history,
        SoqlViewModel soql,
        ApexViewModel apex,
        LogViewModel logs,
        CommandViewModel command,
        ApiConsoleViewModel api,
        DeployViewModel deploy,
        SettingsViewModel settingsViewModel,
        QuickPanelViewModel quickPanel)
    {
        _orgService = orgService;
        _log = log;
        _settings = settings;
        _recentFolders = recentFolders;
        _recentUrls = recentUrls;
        _toolLauncher = toolLauncher;
        _history = historyStore;
        History = history;
        Soql = soql;
        Apex = apex;
        Logs = logs;
        Command = command;
        Api = api;
        Deploy = deploy;
        Settings = settingsViewModel;
        QuickPanel = quickPanel;
        History.ReplayRequested += OnReplayRequested;
        QuickPanel.ExecuteRequested += ExecuteFavorite;
    }

    /// <summary>起動時の初期化（前回状態の復元 → 組織一覧の取得）。</summary>
    public async Task InitializeAsync()
    {
        ReloadRecentFolders();
        ReloadRecentUrls();

        var settings = _settings.Current;
        if (!string.IsNullOrWhiteSpace(settings.LastFolder))
        {
            SelectedFolder = settings.LastFolder;
        }

        await RefreshOrgsAsync();
    }

    [RelayCommand]
    private async Task RefreshOrgsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "組織一覧を取得中…";
        try
        {
            var orgs = await _orgService.ListOrgsAsync();
            var previousUsername = SelectedOrg?.Username;
            var lastUsername = _settings.Current.LastOrgUsername;

            Orgs.Clear();
            foreach (var org in orgs.OrderByDescending(o => o.IsDefault).ThenBy(o => o.DisplayName, StringComparer.CurrentCulture))
            {
                Orgs.Add(org);
            }

            SelectedOrg = Orgs.FirstOrDefault(o => string.Equals(o.Username, previousUsername, StringComparison.OrdinalIgnoreCase))
                          ?? Orgs.FirstOrDefault(o => string.Equals(o.Username, lastUsername, StringComparison.OrdinalIgnoreCase))
                          ?? Orgs.FirstOrDefault(o => o.IsDefault)
                          ?? Orgs.FirstOrDefault();

            StatusMessage = $"組織 {Orgs.Count} 件を取得しました";
            _log.Info($"組織一覧を取得: {Orgs.Count} 件");
        }
        catch (Exception ex)
        {
            StatusMessage = $"組織一覧の取得に失敗: {ex.Message}";
            _log.Error("組織一覧の取得に失敗", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedOrgChanged(OrgInfo? value)
    {
        // 各機能ビューへ対象組織（--target-org / REST 用）を伝播
        var target = value?.Alias ?? value?.Username;
        Soql.CurrentOrg = target;
        Apex.CurrentOrg = target;
        Logs.CurrentOrg = target;
        Command.CurrentOrg = target;
        Api.CurrentOrg = target;
        Deploy.CurrentOrg = target;

        if (value is null || string.Equals(_settings.Current.LastOrgUsername, value.Username, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.Current.LastOrgUsername = value.Username;
        _settings.Save();
    }

    partial void OnSelectedFolderChanged(string? value)
    {
        Soql.CurrentFolder = value;
        Apex.CurrentFolder = value;
        Command.CurrentFolder = value;
        Deploy.CurrentFolder = value;
    }

    private void OnReplayRequested(HistoryEntry entry)
    {
        switch (entry.Type)
        {
            case HistoryTypes.Soql:
                Soql.LoadFromHistory(entry, autoRun: true);
                SelectedTabIndex = 0;
                StatusMessage = "履歴から SOQL を再実行します";
                break;

            case HistoryTypes.Apex:
                Apex.LoadFromHistory(entry, autoRun: false);
                SelectedTabIndex = 1;
                StatusMessage = "履歴から匿名Apex を読み込みました（Ctrl+Enter で実行）";
                break;

            case HistoryTypes.Command:
                Command.LoadFromHistory(entry, autoRun: false);
                SelectedTabIndex = 5;
                StatusMessage = "履歴からコマンドを読み込みました（実行ボタンで再実行）";
                break;

            case HistoryTypes.Api:
                Api.LoadFromHistory(entry, autoRun: false);
                SelectedTabIndex = 6;
                StatusMessage = "履歴から REST リクエストを読み込みました（送信ボタンで再実行）";
                break;

            case HistoryTypes.Deploy:
                Deploy.LoadFromHistory(entry, autoRun: false);
                SelectedTabIndex = 4;
                StatusMessage = "履歴からデプロイ設定を読み込みました（実行ボタンで再実行）";
                break;

            default:
                StatusMessage = $"「{entry.TypeLabel}」の再実行には対応していません";
                break;
        }
    }

    /// <summary>クイックパネル / Ctrl+1..9 からお気に入りを実行する。</summary>
    public void ExecuteFavorite(FavoriteItem favorite)
    {
        switch (favorite.Type.ToLowerInvariant())
        {
            case HistoryTypes.Soql:
                Soql.LoadFavorite(favorite, autoRun: true);
                SelectedTabIndex = 0;
                StatusMessage = $"お気に入りを実行: {favorite.Label}";
                break;

            case HistoryTypes.Apex:
                Apex.LoadFavorite(favorite, autoRun: false);
                SelectedTabIndex = 1;
                StatusMessage = $"お気に入りを読み込みました（Ctrl+Enter で実行）: {favorite.Label}";
                break;

            case HistoryTypes.Command:
                Command.LoadFavorite(favorite, autoRun: false);
                SelectedTabIndex = 5;
                StatusMessage = $"お気に入りを読み込みました（実行ボタンで再実行）: {favorite.Label}";
                break;

            case HistoryTypes.Api:
                Api.LoadFavorite(favorite, autoRun: false);
                SelectedTabIndex = 6;
                StatusMessage = $"お気に入りを読み込みました（送信ボタンで再実行）: {favorite.Label}";
                break;

            case HistoryTypes.Deploy:
                Deploy.LoadFavorite(favorite, autoRun: false);
                SelectedTabIndex = 4;
                StatusMessage = $"お気に入りを読み込みました（実行ボタンで再実行）: {favorite.Label}";
                break;

            case "url":
                LaunchUrl(favorite.Payload);
                break;

            case "folder":
                CommitFolder(favorite.Payload);
                break;

            default:
                StatusMessage = $"「{favorite.Type}」のお気に入りには対応していません";
                break;
        }
    }

    /// <summary>Ctrl+数字（1〜9）でクイックパネルのスロットを実行する。</summary>
    public void ExecuteQuickSlot(int number)
    {
        var favorite = QuickPanel.GetSlot(number);
        if (favorite is null)
        {
            StatusMessage = $"お気に入り {number} は未登録です";
            return;
        }

        ExecuteFavorite(favorite);
    }

    /// <summary>F5: 直前の操作を再実行する（種別ごとの安全な再実行）。</summary>
    public void ReplayLastOperation()
    {
        var last = _history.Query().FirstOrDefault();
        if (last is null)
        {
            StatusMessage = "再実行できる履歴がありません";
            return;
        }

        OnReplayRequested(last);
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "SF 実行フォルダを選択" };
        if (!string.IsNullOrWhiteSpace(SelectedFolder) && Directory.Exists(SelectedFolder))
        {
            dialog.InitialDirectory = SelectedFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            CommitFolder(dialog.FolderName);
        }
    }

    /// <summary>フォルダを確定する（最近使用リストへの記録 + 設定保存）。</summary>
    public void CommitFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        path = path.Trim();
        if (!Directory.Exists(path))
        {
            StatusMessage = $"フォルダが存在しません: {path}";
            return;
        }

        SelectedFolder = path;
        _recentFolders.Touch(path);
        _settings.Current.LastFolder = path;
        _settings.Save();
        ReloadRecentFolders();
        StatusMessage = $"SFフォルダ: {path}";
    }

    // ---- ツールランチャー ----

    /// <summary>ターミナルを起動（kind: wt / powershell / cmd / wsl）。</summary>
    [RelayCommand]
    private void OpenTerminal(string? kind)
    {
        var folder = ResolveLaunchFolder();
        var result = _toolLauncher.LaunchTerminal(folder, kind ?? "wt");
        StatusMessage = result.Success ? $"ターミナル: {folder}" : result.Message;

        if (result.Success)
        {
            _log.Info($"ターミナル起動 ({kind ?? "wt"}): {folder}");
        }
        else
        {
            _log.Warn(result.Message);
        }
    }

    /// <summary>エクスプローラーで開く（mode=copy-path でパスをコピー）。</summary>
    [RelayCommand]
    private void OpenExplorer(string? mode)
    {
        if (mode == "copy-path")
        {
            try
            {
                Clipboard.SetText(ResolveLaunchFolder());
                StatusMessage = "フォルダパスをコピーしました";
            }
            catch (Exception ex)
            {
                StatusMessage = $"コピーに失敗: {ex.Message}";
            }

            return;
        }

        var folder = ResolveLaunchFolder();
        var result = _toolLauncher.LaunchExplorer(folder);
        StatusMessage = result.Success ? $"エクスプローラー: {folder}" : result.Message;
    }

    /// <summary>VS Code で開く（mode: new / reuse）。</summary>
    [RelayCommand]
    private void OpenVsCode(string? mode)
    {
        var folder = ResolveLaunchFolder();
        var result = _toolLauncher.LaunchVsCode(folder, mode ?? "new");
        StatusMessage = result.Success ? $"VS Code: {folder}" : result.Message;
    }

    /// <summary>ブラウザで開く（target: org-home / org-setup / login / input / url:...）。</summary>
    [RelayCommand]
    private async Task OpenBrowserAsync(string? target)
    {
        target ??= "org-home";
        try
        {
            switch (target)
            {
                case "input":
                    var input = InputBox.Show(
                        "URL を入力",
                        "開く URL を入力してください（例: https://hks3.my.salesforce.com/lightning/o/Account/list）");
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        LaunchUrl(input.Trim());
                    }

                    break;

                case "login":
                    LaunchUrl("https://login.salesforce.com");
                    break;

                case "org-home":
                case "org-setup":
                    var org = Soql.CurrentOrg;
                    if (string.IsNullOrWhiteSpace(org))
                    {
                        StatusMessage = "上部バーで組織を選択してください";
                        return;
                    }

                    StatusMessage = "組織の URL を取得中…";
                    var auth = await _orgService.GetAuthAsync(org);
                    if (string.IsNullOrWhiteSpace(auth.InstanceUrl))
                    {
                        StatusMessage = "組織の instanceUrl を取得できませんでした";
                        return;
                    }

                    var path = target == "org-setup" ? "/lightning/setup/SetupOneHome/home" : "/lightning/page/home";
                    LaunchUrl(auth.InstanceUrl.TrimEnd('/') + path);
                    break;

                default:
                    if (target.StartsWith("url:", StringComparison.OrdinalIgnoreCase))
                    {
                        LaunchUrl(target[4..]);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"ブラウザ起動に失敗: {ex.Message}";
            _log.Error("ブラウザ起動に失敗", ex);
        }
    }

    /// <summary>最近の URL を開く。</summary>
    [RelayCommand]
    private void OpenRecentUrl(RecentUrl? item)
    {
        if (item is not null)
        {
            LaunchUrl(item.Url);
        }
    }

    private void LaunchUrl(string url)
    {
        var result = _toolLauncher.LaunchBrowser(url);
        if (result.Success)
        {
            _recentUrls.Touch(url);
            ReloadRecentUrls();
            StatusMessage = $"ブラウザ: {url}";
        }
        else
        {
            StatusMessage = result.Message;
        }
    }

    /// <summary>起動に使うフォルダ（未選択ならユーザープロファイル）。</summary>
    private string ResolveLaunchFolder()
    {
        if (!string.IsNullOrWhiteSpace(SelectedFolder) && Directory.Exists(SelectedFolder))
        {
            return SelectedFolder;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private void ReloadRecentFolders()
    {
        RecentFolders.Clear();
        foreach (var folder in _recentFolders.GetOrdered())
        {
            RecentFolders.Add(folder);
        }
    }

    private void ReloadRecentUrls()
    {
        RecentUrls.Clear();
        foreach (var url in _recentUrls.GetOrdered().Take(10))
        {
            RecentUrls.Add(url);
        }
    }
}
