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
    private string _confirmPolicyLabel = "危険操作のみ確認";

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _detectedSummary = string.Empty;

    /// <summary>確認ポリシーの選択肢（日本語ラベル）。</summary>
    public IReadOnlyList<string> ConfirmPolicyLabels { get; } = new[]
    {
        "危険操作のみ確認", "常に確認", "確認しない",
    };

    /// <summary>データフォルダの表示用。</summary>
    public string DataRootText => $"データフォルダ: {_paths.DataRoot}";

    public SettingsViewModel(
        AppSettingsStore settings,
        SfCliRunner sfRunner,
        ToolLauncherService toolLauncher,
        HistoryStore history,
        AppPaths paths,
        AppLog log)
    {
        _settings = settings;
        _sfRunner = sfRunner;
        _toolLauncher = toolLauncher;
        _history = history;
        _paths = paths;
        _log = log;
        Load();
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
        RefreshDetectedSummary();
        StatusText = "現在の設定を読み込みました";
    }

    private void RefreshDetectedSummary()
        => DetectedSummary =
            $"自動検出: sf={SfCliRunner.ResolveSfPath() ?? "未検出"} / wt={ToolLauncherService.ResolveWindowsTerminalPath() ?? "未検出"} / code={ToolLauncherService.ResolveVsCodeCliPath() ?? "未検出"}";

    [RelayCommand]
    private void Reload() => Load();

    [RelayCommand]
    private void BrowseSf()
    {
        var dialog = new OpenFileDialog
        {
            Title = "sf 実行ファイルを選択（sf.cmd）",
            Filter = "sf コマンド (*.cmd;*.exe;*.bat)|*.cmd;*.exe;*.bat|すべてのファイル (*.*)|*.*",
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
            Title = "Windows Terminal (wt.exe) を選択",
            Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*",
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
            Title = "VS Code CLI (code.cmd) を選択",
            Filter = "code (*.cmd;*.exe;*.bat)|*.cmd;*.exe;*.bat|すべてのファイル (*.*)|*.*",
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
            "代表的なサンプル（SOQL / 匿名Apex / コマンド / REST API）を履歴に追加しますか？" + Environment.NewLine
            + "同じ内容が既にある場合はスキップされます。",
            "SfUi",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var result = SampleHistorySeeder.Seed(_history, _log);
        StatusText = $"サンプル履歴: 追加 {result.Added} 件 / スキップ {result.Skipped} 件";
    }

    [RelayCommand]
    private void OpenDataFolder() => _toolLauncher.LaunchExplorer(_paths.DataRoot);

    [RelayCommand]
    private void Save()
    {
        if (!int.TryParse(HistoryLimitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) || limit < 1)
        {
            StatusText = "履歴上限は 1 以上の整数で入力してください";
            return;
        }

        if (!long.TryParse(ThresholdKbText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var thresholdKb) || thresholdKb < 0)
        {
            StatusText = "結果保存閾値は 0 以上の整数（KB）で入力してください";
            return;
        }

        var s = _settings.Current;
        s.SfExecutablePath = NullIfEmpty(SfPath);
        s.WindowsTerminalPath = NullIfEmpty(TerminalPath);
        s.VsCodePath = NullIfEmpty(VsCodePath);
        s.MaxHistoryPerType = limit;
        s.ResultInlineThresholdBytes = thresholdKb * 1024;
        s.ConfirmPolicy = ConfirmPolicies.FromLabel(ConfirmPolicyLabel);
        _settings.Save();

        var resolved = _sfRunner.SetExecutablePath(s.SfExecutablePath);
        RefreshDetectedSummary();

        StatusText = resolved is null
            ? "保存しました（sf が見つかりません。パスを確認してください）"
            : $"保存しました / sf: {resolved}";
        _log.Info($"設定を保存: 履歴上限={limit} 件 / 閾値={thresholdKb} KB / 確認ポリシー={s.ConfirmPolicy}");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
