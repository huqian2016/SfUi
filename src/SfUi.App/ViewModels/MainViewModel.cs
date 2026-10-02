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
    private string _statusMessage = UiText.T("Common_Ready");

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

    /// <summary>言語コンボの選択値（English / 日本語）。</summary>
    [ObservableProperty]
    private string _languageLabel = "English";

    /// <summary>言語コンボの選択肢。</summary>
    public IReadOnlyList<string> LanguageLabels { get; } = new[] { "English", "日本語" };

    /// <summary>ステータスバー表示用（組織・フォルダ）。</summary>
    public string StatusDetail => UiText.T("Main_StatusDetailFmt", SelectedOrg?.DisplayName ?? UiText.T("Main_NotSelected"), SelectedFolder ?? UiText.T("Main_DefaultFolder"));

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

    /// <summary>AI チャットタブの ViewModel。</summary>
    public AiChatViewModel Ai { get; }

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
        AiChatViewModel ai,
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
        Ai = ai;
        Logs = logs;
        Command = command;
        Api = api;
        Deploy = deploy;
        Settings = settingsViewModel;
        QuickPanel = quickPanel;
        History.ReplayRequested += OnReplayRequested;
        QuickPanel.ExecuteRequested += ExecuteFavorite;
        Ai.ApplyRequested += ApplyAiSnippet;

        // 保存済みの言語をコンボへ反映（起動時の UiText 適用は App 側で実施済み）
        _languageLabel = string.Equals(_settings.Current.Language, UiText.Japanese, StringComparison.OrdinalIgnoreCase) ? "日本語" : "English";
        OnPropertyChanged(nameof(LanguageLabel));
    }

    partial void OnLanguageLabelChanged(string value)
    {
        var code = string.Equals(value, "日本語", StringComparison.Ordinal) ? UiText.Japanese : UiText.English;
        if (code == UiText.Language)
        {
            return;
        }

        UiText.SetLanguage(code);
        _settings.Current.Language = code;
        _settings.Save();
        StatusMessage = UiText.T("Msg_LanguageFmt", value);
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
        StatusMessage = UiText.T("Msg_LoadingOrgs");
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

            StatusMessage = UiText.T("Msg_OrgCountFmt", Orgs.Count);
            _log.Info($"組織一覧を取得: {Orgs.Count} 件");
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Msg_OrgLoadFailedFmt", ex.Message);
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
        Ai.CurrentOrg = target;
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
                StatusMessage = UiText.T("Msg_ReplaySoql");
                break;

            case HistoryTypes.Apex:
                Apex.LoadFromHistory(entry, autoRun: false);
                SelectedTabIndex = 1;
                StatusMessage = UiText.T("Msg_ReplayApex");
                break;

            case HistoryTypes.Command:
                Command.LoadFromHistory(entry, autoRun: false);
                SelectedTabIndex = 6;
                StatusMessage = UiText.T("Msg_ReplayCommand");
                break;

            case HistoryTypes.Api:
                Api.LoadFromHistory(entry, autoRun: false);
                SelectedTabIndex = 7;
                StatusMessage = UiText.T("Msg_ReplayApi");
                break;

            case HistoryTypes.Deploy:
                Deploy.LoadFromHistory(entry, autoRun: false);
                SelectedTabIndex = 5;
                StatusMessage = UiText.T("Msg_ReplayDeploy");
                break;

            default:
                StatusMessage = UiText.T("Msg_ReplayUnsupportedFmt", entry.TypeLabel);
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
                StatusMessage = UiText.T("Msg_FavoriteRunFmt", favorite.Label);
                break;

            case HistoryTypes.Apex:
                Apex.LoadFavorite(favorite, autoRun: false);
                SelectedTabIndex = 1;
                StatusMessage = UiText.T("Msg_FavoriteLoadedApexFmt", favorite.Label);
                break;

            case HistoryTypes.Command:
                Command.LoadFavorite(favorite, autoRun: false);
                SelectedTabIndex = 6;
                StatusMessage = UiText.T("Msg_FavoriteLoadedCommandFmt", favorite.Label);
                break;

            case HistoryTypes.Api:
                Api.LoadFavorite(favorite, autoRun: false);
                SelectedTabIndex = 7;
                StatusMessage = UiText.T("Msg_FavoriteLoadedApiFmt", favorite.Label);
                break;

            case HistoryTypes.Deploy:
                Deploy.LoadFavorite(favorite, autoRun: false);
                SelectedTabIndex = 5;
                StatusMessage = UiText.T("Msg_FavoriteLoadedDeployFmt", favorite.Label);
                break;

            case "url":
                LaunchUrl(favorite.Payload);
                break;

            case "folder":
                CommitFolder(favorite.Payload);
                break;

            default:
                StatusMessage = UiText.T("Msg_FavoriteUnsupportedFmt", favorite.Type);
                break;
        }
    }

    /// <summary>Ctrl+数字（1〜9）でクイックパネルのスロットを実行する。</summary>
    public void ExecuteQuickSlot(int number)
    {
        var favorite = QuickPanel.GetSlot(number);
        if (favorite is null)
        {
            StatusMessage = UiText.T("Msg_QuickSlotEmptyFmt", number);
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
            StatusMessage = UiText.T("Msg_NoHistory");
            return;
        }

        OnReplayRequested(last);
    }

    /// <summary>AI が生成したコード片を該当タブへ読み込む。</summary>
    private void ApplyAiSnippet(AiSnippet snippet)
    {
        switch (snippet.Language)
        {
            case "soql":
                Soql.LoadText(snippet.Code);
                SelectedTabIndex = 0;
                StatusMessage = UiText.T("Ai_AppliedSoql");
                break;

            case "apex":
                Apex.LoadText(snippet.Code);
                SelectedTabIndex = 1;
                StatusMessage = UiText.T("Ai_AppliedApex");
                break;

            case "command":
                Command.LoadText(snippet.Code);
                SelectedTabIndex = 6;
                StatusMessage = UiText.T("Ai_AppliedCommand");
                break;
        }
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog { Title = UiText.T("Dlg_BrowseFolderTitle") };
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
            StatusMessage = UiText.T("Msg_FolderNotExistFmt", path);
            return;
        }

        SelectedFolder = path;
        _recentFolders.Touch(path);
        _settings.Current.LastFolder = path;
        _settings.Save();
        ReloadRecentFolders();
        StatusMessage = UiText.T("Msg_FolderSetFmt", path);
    }

    // ---- ツールランチャー ----

    /// <summary>ターミナルを起動（kind: wt / powershell / cmd / wsl）。</summary>
    [RelayCommand]
    private void OpenTerminal(string? kind)
    {
        var folder = ResolveLaunchFolder();
        var result = _toolLauncher.LaunchTerminal(folder, kind ?? "wt");
        StatusMessage = result.Success ? UiText.T("Msg_TerminalFmt", folder) : result.Message;

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
                StatusMessage = UiText.T("Msg_Copied");
            }
            catch (Exception ex)
            {
                StatusMessage = UiText.T("Msg_CopyFailedFmt", ex.Message);
            }

            return;
        }

        var folder = ResolveLaunchFolder();
        var result = _toolLauncher.LaunchExplorer(folder);
        StatusMessage = result.Success ? UiText.T("Msg_ExplorerFmt", folder) : result.Message;
    }

    /// <summary>VS Code で開く（mode: new / reuse）。</summary>
    [RelayCommand]
    private void OpenVsCode(string? mode)
    {
        var folder = ResolveLaunchFolder();
        var result = _toolLauncher.LaunchVsCode(folder, mode ?? "new");
        StatusMessage = result.Success ? UiText.T("Msg_VsCodeFmt", folder) : result.Message;
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
                        UiText.T("Msg_InputUrlTitle"),
                        UiText.T("Msg_InputUrlPrompt"));
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
                        StatusMessage = UiText.T("Msg_SelectOrg");
                        return;
                    }

                    StatusMessage = UiText.T("Msg_GetOrgUrl");
                    var auth = await _orgService.GetAuthAsync(org);
                    if (string.IsNullOrWhiteSpace(auth.InstanceUrl))
                    {
                        StatusMessage = UiText.T("Msg_NoInstanceUrl");
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
            StatusMessage = UiText.T("Msg_BrowserFailedFmt", ex.Message);
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
            StatusMessage = UiText.T("Msg_BrowserFmt", url);
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
