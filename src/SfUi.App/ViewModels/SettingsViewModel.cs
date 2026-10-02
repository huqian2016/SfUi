using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>設定タブの ViewModel（sf パス / ツールパス / 履歴上限と閾値 / 実行前確認ポリシー）。</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _settings;
    private readonly SfCliRunner _sfRunner;
    private readonly ToolLauncherService _toolLauncher;
    private readonly HistoryStore _history;
    private readonly DeepSeekClient _deepSeek;
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    [ObservableProperty]
    private string? _sfPath;

    [ObservableProperty]
    private string? _terminalPath;

    [ObservableProperty]
    private string? _vsCodePath;

    [ObservableProperty]
    private string _historyLimitText = "2000";

    [ObservableProperty]
    private string _thresholdKbText = "64";

    [ObservableProperty]
    private string _confirmPolicyLabel = UiText.T("Policy_Dangerous");

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _detectedSummary = string.Empty;

    [ObservableProperty]
    private string? _apiKey;

    [ObservableProperty]
    private string _aiModel = DeepSeekClient.DefaultModel;

    /// <summary>DeepSeek モデルの選択肢。</summary>
    public IReadOnlyList<string> AiModels { get; } = new[] { "deepseek-chat", "deepseek-reasoner" };

    /// <summary>確認ポリシーの選択肢（言語切替で再構築される）。</summary>
    public ObservableCollection<string> ConfirmPolicyLabels { get; } = new();

    /// <summary>データフォルダの表示用。</summary>
    public string DataRootText => UiText.T("Settings_DataRootFmt", _paths.DataRoot);

    public SettingsViewModel(
        AppSettingsStore settings,
        SfCliRunner sfRunner,
        ToolLauncherService toolLauncher,
        HistoryStore history,
        DeepSeekClient deepSeek,
        AppPaths paths,
        AppLog log)
    {
        _settings = settings;
        _sfRunner = sfRunner;
        _toolLauncher = toolLauncher;
        _history = history;
        _deepSeek = deepSeek;
        _paths = paths;
        _log = log;
        UiText.LanguageChanged += OnLanguageChanged;
        BuildPolicyLabels();
        Load();
    }

    private void OnLanguageChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        void Apply()
        {
            var policy = ConfirmPolicies.FromLabel(ConfirmPolicyLabel);
            BuildPolicyLabels();
            ConfirmPolicyLabel = ConfirmPolicies.ToLabel(policy);
            RefreshDetectedSummary();
            OnPropertyChanged(nameof(DataRootText));
            StatusText = UiText.T("Settings_Loaded");
        }

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            dispatcher.Invoke(Apply);
        }
    }

    private void BuildPolicyLabels()
    {
        var current = ConfirmPolicies.FromLabel(ConfirmPolicyLabel);
        ConfirmPolicyLabels.Clear();
        ConfirmPolicyLabels.Add(ConfirmPolicies.ToLabel(ConfirmPolicies.Dangerous));
        ConfirmPolicyLabels.Add(ConfirmPolicies.ToLabel(ConfirmPolicies.Always));
        ConfirmPolicyLabels.Add(ConfirmPolicies.ToLabel(ConfirmPolicies.Never));
        ConfirmPolicyLabel = ConfirmPolicies.ToLabel(current);
    }

    /// <summary>現在の設定値をフォームへ読み込む。</summary>
    public void Load()
    {
        var s = _settings.Current;
        SfPath = s.SfExecutablePath;
        TerminalPath = s.WindowsTerminalPath;
        VsCodePath = s.VsCodePath;
        HistoryLimitText = s.MaxHistoryPerType.ToString(CultureInfo.InvariantCulture);
        ThresholdKbText = Math.Max(0, s.ResultInlineThresholdBytes / 1024).ToString(CultureInfo.InvariantCulture);
        ConfirmPolicyLabel = ConfirmPolicies.ToLabel(s.ConfirmPolicy);
        ApiKey = s.DeepSeekApiKey;
        AiModel = string.IsNullOrWhiteSpace(s.DeepSeekModel) ? DeepSeekClient.DefaultModel : s.DeepSeekModel;
        RefreshDetectedSummary();
        StatusText = UiText.T("Settings_Loaded");
    }

    private void RefreshDetectedSummary()
        => DetectedSummary = UiText.T("Settings_DetectedFmt",
            SfCliRunner.ResolveSfPath() ?? UiText.T("Common_NotFound"),
            ToolLauncherService.ResolveWindowsTerminalPath() ?? UiText.T("Common_NotFound"),
            ToolLauncherService.ResolveVsCodeCliPath() ?? UiText.T("Common_NotFound"));

    [RelayCommand]
    private void Reload() => Load();

    [RelayCommand]
    private void BrowseSf()
    {
        var dialog = new OpenFileDialog
        {
            Title = UiText.T("Settings_BrowseSfTitle"),
            Filter = UiText.T("Settings_SfFilter"),
        };
        if (dialog.ShowDialog() == true)
        {
            SfPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseTerminal()
    {
        var dialog = new OpenFileDialog
        {
            Title = UiText.T("Settings_BrowseTerminalTitle"),
            Filter = UiText.T("Settings_ExeFilter"),
        };
        if (dialog.ShowDialog() == true)
        {
            TerminalPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseVsCode()
    {
        var dialog = new OpenFileDialog
        {
            Title = UiText.T("Settings_BrowseVsCodeTitle"),
            Filter = UiText.T("Settings_CodeFilter"),
        };
        if (dialog.ShowDialog() == true)
        {
            VsCodePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void ClearSfPath() => SfPath = null;

    /// <summary>代表的なサンプル（SOQL / 匿名Apex / コマンド / REST）を履歴に追加する。</summary>
    [RelayCommand]
    private void SeedSamples()
    {
        var answer = MessageBox.Show(
            UiText.T("Settings_SeedConfirmFmt", Environment.NewLine),
            "SfUi",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var result = SampleHistorySeeder.Seed(_history, _log);
        StatusText = UiText.T("Settings_SeedDoneFmt", result.Added, result.Skipped);
    }

    /// <summary>DeepSeek API への接続をテストする（入力中の値を一時的に適用）。</summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        _settings.Current.DeepSeekApiKey = NullIfEmpty(ApiKey);
        _settings.Current.DeepSeekModel = string.IsNullOrWhiteSpace(AiModel) ? DeepSeekClient.DefaultModel : AiModel.Trim();
        StatusText = UiText.T("Ai_Thinking");
        var result = await _deepSeek.ChatAsync(new[] { new DeepSeekClient.ChatMessage("user", "Reply with the single word: OK") });
        StatusText = result.Success
            ? UiText.T("Settings_TestOkFmt", result.Content?.Trim() ?? "OK")
            : UiText.T("Settings_TestFailedFmt", result.Error);
    }

    [RelayCommand]
    private void OpenDataFolder() => _toolLauncher.LaunchExplorer(_paths.DataRoot);

    [RelayCommand]
    private void Save()
    {
        if (!int.TryParse(HistoryLimitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) || limit < 1)
        {
            StatusText = UiText.T("Settings_InvalidHistoryLimit");
            return;
        }

        if (!long.TryParse(ThresholdKbText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var thresholdKb) || thresholdKb < 0)
        {
            StatusText = UiText.T("Settings_InvalidThreshold");
            return;
        }

        var s = _settings.Current;
        s.SfExecutablePath = NullIfEmpty(SfPath);
        s.WindowsTerminalPath = NullIfEmpty(TerminalPath);
        s.VsCodePath = NullIfEmpty(VsCodePath);
        s.MaxHistoryPerType = limit;
        s.ResultInlineThresholdBytes = thresholdKb * 1024;
        s.ConfirmPolicy = ConfirmPolicies.FromLabel(ConfirmPolicyLabel);
        s.DeepSeekApiKey = NullIfEmpty(ApiKey);
        s.DeepSeekModel = string.IsNullOrWhiteSpace(AiModel) ? DeepSeekClient.DefaultModel : AiModel.Trim();
        _settings.Save();

        var resolved = _sfRunner.SetExecutablePath(s.SfExecutablePath);
        RefreshDetectedSummary();

        StatusText = resolved is null
            ? UiText.T("Settings_SavedSfMissing")
            : UiText.T("Settings_SavedSfFmt", resolved);
        _log.Info($"設定を保存: 履歴上限={limit} 件 / 閾値={thresholdKb} KB / 確認ポリシー={s.ConfirmPolicy}");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
