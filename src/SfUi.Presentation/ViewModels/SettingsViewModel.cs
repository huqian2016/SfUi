using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>設定タブの ViewModel（sf パス / ツールパス / 履歴上限と閾値 / 実行前確認ポリシー）。</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _settings;
    private readonly SfCliRunner _sfRunner;
    private readonly ToolLauncherService _toolLauncher;
    private readonly HistoryStore _history;
    private readonly AiChatClient _ai;
    private readonly AppPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _filePicker;
    private readonly IUiDispatcher _ui;
    private readonly AppLog _log;
    private readonly IAppWindowService _windows;

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

    /// <summary>接続先エンドポイント（null / 空 = DeepSeek 既定）。</summary>
    [ObservableProperty]
    private string? _apiEndpoint;

    [ObservableProperty]
    private string _aiModel = AiChatClient.DefaultModel;

    /// <summary>確認ポリシーの選択肢（言語切替で再構築される）。</summary>
    public ObservableCollection<string> ConfirmPolicyLabels { get; } = new();

    /// <summary>データフォルダの表示用。</summary>
    public string DataRootText => UiText.T("Settings_DataRootFmt", _paths.DataRoot);

    public SettingsViewModel(
        AppSettingsStore settings,
        SfCliRunner sfRunner,
        ToolLauncherService toolLauncher,
        HistoryStore history,
        AiChatClient ai,
        AppPaths paths,
        IDialogService dialogs,
        IFilePickerService filePicker,
        IUiDispatcher ui,
        AppLog log,
        IAppWindowService windows)
    {
        _settings = settings;
        _sfRunner = sfRunner;
        _toolLauncher = toolLauncher;
        _history = history;
        _ai = ai;
        _paths = paths;
        _dialogs = dialogs;
        _filePicker = filePicker;
        _ui = ui;
        _log = log;
        _windows = windows;
        UiText.LanguageChanged += OnLanguageChanged;
        BuildPolicyLabels();
        Load();
    }

    private void OnLanguageChanged()
    {
        void Apply()
        {
            var policy = ConfirmPolicies.FromLabel(ConfirmPolicyLabel);
            BuildPolicyLabels();
            ConfirmPolicyLabel = ConfirmPolicies.ToLabel(policy);
            RefreshDetectedSummary();
            OnPropertyChanged(nameof(DataRootText));
            StatusText = UiText.T("Settings_Loaded");
        }

        if (_ui.CheckAccess())
        {
            Apply();
        }
        else
        {
            _ui.Invoke(Apply);
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
        ApiEndpoint = s.AiEndpoint;
        ApiKey = s.AiApiKey;
        AiModel = AiChatClient.ResolveModel(s.AiModel);
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
    private async Task BrowseSfAsync()
    {
        var fileName = await _filePicker.OpenFileAsync(UiText.T("Settings_BrowseSfTitle"), UiText.T("Settings_SfFilter"));
        if (fileName is not null)
        {
            SfPath = fileName;
        }
    }

    [RelayCommand]
    private async Task BrowseTerminalAsync()
    {
        var fileName = await _filePicker.OpenFileAsync(UiText.T("Settings_BrowseTerminalTitle"), UiText.T("Settings_ExeFilter"));
        if (fileName is not null)
        {
            TerminalPath = fileName;
        }
    }

    [RelayCommand]
    private async Task BrowseVsCodeAsync()
    {
        var fileName = await _filePicker.OpenFileAsync(UiText.T("Settings_BrowseVsCodeTitle"), UiText.T("Settings_CodeFilter"));
        if (fileName is not null)
        {
            VsCodePath = fileName;
        }
    }

    [RelayCommand]
    private void ClearSfPath() => SfPath = null;

    /// <summary>代表的なサンプル（SOQL / 匿名Apex / コマンド / REST）を履歴に追加する。</summary>
    [RelayCommand]
    private void SeedSamples()
    {
        var answer = _dialogs.Confirm(
            UiText.T("Settings_SeedConfirmFmt", Environment.NewLine),
            "SfUi");
        if (!answer)
        {
            return;
        }

        var result = SampleHistorySeeder.Seed(_history, _log);
        StatusText = UiText.T("Settings_SeedDoneFmt", result.Added, result.Skipped);
    }

    /// <summary>DeepSeek 既定（エンドポイント空欄 + 既定モデル）。</summary>
    [RelayCommand]
    private void UseDeepSeekPreset()
    {
        ApiEndpoint = null;
        AiModel = AiChatClient.DefaultModel;
    }

    /// <summary>OpenAI（OpenAI 互換 API）。</summary>
    [RelayCommand]
    private void UseOpenAiPreset()
    {
        ApiEndpoint = "https://api.openai.com/v1/chat/completions";
        AiModel = "gpt-4o-mini";
    }

    /// <summary>Anthropic（Claude）。Anthropic の OpenAI 互換レイヤーを使用する。</summary>
    [RelayCommand]
    private void UseAnthropicPreset()
    {
        ApiEndpoint = "https://api.anthropic.com/v1/chat/completions";
        AiModel = "claude-sonnet-4-6";
    }

    /// <summary>ローカル LLM（Ollama / LM Studio の OpenAI 互換 API。API キー不要）。</summary>
    [RelayCommand]
    private void UseLocalPreset()
    {
        ApiEndpoint = "http://localhost:11434/v1/chat/completions";
        AiModel = "llama3.1";
    }

    /// <summary>AI 接続先への接続をテストする（入力中の値を一時的に適用）。</summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        _settings.Current.AiEndpoint = NullIfEmpty(ApiEndpoint);
        _settings.Current.AiApiKey = NullIfEmpty(ApiKey);
        _settings.Current.AiModel = string.IsNullOrWhiteSpace(AiModel) ? null : AiModel.Trim();
        StatusText = UiText.T("Ai_Thinking");
        var result = await _ai.ChatAsync(new[] { new AiChatClient.ChatMessage("user", "Reply with the single word: OK") });
        StatusText = result.Success
            ? UiText.T("Settings_TestOkFmt", result.Content?.Trim() ?? "OK")
            : UiText.T("Settings_TestFailedFmt", result.Error);
    }

    [RelayCommand]
    private void OpenDataFolder() => _toolLauncher.LaunchExplorer(_paths.DataRoot);

    /// <summary>ようこそ画面を再表示する（「今後表示しない」の切替もこの画面で行える）。</summary>
    [RelayCommand]
    private void OpenWelcome()
    {
        _log.Info("設定: ようこそ画面を表示");
        _windows.OpenWelcome(null);
    }

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
        s.AiEndpoint = NullIfEmpty(ApiEndpoint);
        s.AiApiKey = NullIfEmpty(ApiKey);
        s.AiModel = string.IsNullOrWhiteSpace(AiModel) ? null : AiModel.Trim();
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
