using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>ようこそ画面（機能紹介 + Salesforce CLI の検出 / インストール案内）の ViewModel。</summary>
public partial class WelcomeViewModel : ObservableObject
{
    /// <summary>Salesforce CLI 公式ツール ページ（インストーラーのダウンロード先）。</summary>
    public const string InstallDocsUrl = "https://developer.salesforce.com/tools/salesforcecli";

    private readonly AppSettingsStore _settings;
    private readonly SfCliRunner _sf;
    private readonly ToolLauncherService _toolLauncher;
    private readonly StartupOptions _options;

    public WelcomeViewModel(AppSettingsStore settings, SfCliRunner sf, ToolLauncherService toolLauncher, StartupOptions options)
    {
        _settings = settings;
        _sf = sf;
        _toolLauncher = toolLauncher;
        _options = options;

        _dontShowAgain = settings.Current.WelcomeDismissed;
        RefreshSfState();
    }

    /// <summary>「使い始める」「設定を開く」でウィンドウを閉じてもらうための通知。</summary>
    public event Action? CloseRequested;

    /// <summary>「設定を開く」が選ばれた（ウィンドウを閉じた後にメイン画面の設定タブへ移動する）。</summary>
    public event Action? OpenSettingsRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SfMissing))]
    private bool _sfFound;

    [ObservableProperty]
    private string _sfPathText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Salesforce CLI が見つからない（未検出 UI を表示する）。</summary>
    public bool SfMissing => !SfFound;

    /// <summary>検出済み表示（パス付き）。</summary>
    public string SfFoundText => UiText.T("Welcome_SfFoundFmt", SfPathText);

    /// <summary>「今後表示しない」（チェックで次回起動から非表示。設定タブからの再表示で外せる）。</summary>
    [ObservableProperty]
    private bool _dontShowAgain;

    partial void OnDontShowAgainChanged(bool value)
    {
        _settings.Current.WelcomeDismissed = value;
        _settings.Save();
    }

    partial void OnSfPathTextChanged(string value) => OnPropertyChanged(nameof(SfFoundText));

    /// <summary>公式インストーラー ページをブラウザで開く。</summary>
    [RelayCommand]
    private void OpenInstallPage()
    {
        var result = _toolLauncher.LaunchBrowser(InstallDocsUrl);
        if (!result.Success)
        {
            StatusText = result.Message;
        }
    }

    /// <summary>sf を再検出する（インストール直後に再起動なしで以降の実行へ反映）。</summary>
    [RelayCommand]
    private void Recheck()
    {
        RefreshSfState();
        StatusText = SfFound
            ? UiText.T("Welcome_SfRecheckOkFmt", SfPathText)
            : UiText.T("Welcome_SfRecheckNg");
    }

    /// <summary>「使い始める」: ウィンドウを閉じる。</summary>
    [RelayCommand]
    private void Start() => CloseRequested?.Invoke();

    /// <summary>「設定を開く」: 設定タブへの遷移を通知して閉じる。</summary>
    [RelayCommand]
    private void OpenSettings()
    {
        OpenSettingsRequested?.Invoke();
        CloseRequested?.Invoke();
    }

    /// <summary>sf 実行ファイルを解決する（--welcome-missing 時は未検出として扱い、実際の検出は行わない）。</summary>
    private void RefreshSfState()
    {
        string? path = null;
        if (!_options.SimulateSfMissing)
        {
            path = SfCliRunner.ResolveSfPath(_settings.Current.SfExecutablePath);
            _sf.SetExecutablePath(path);
        }

        SfFound = path is not null;
        SfPathText = path ?? string.Empty;
    }
}
