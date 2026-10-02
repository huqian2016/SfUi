namespace SfUi.Core;

/// <summary>アプリ設定（settings.json）。手で編集できることを想定したシンプルな構造。</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>前回選択していた組織のユーザー名（起動時の復元用）。</summary>
    public string? LastOrgUsername { get; set; }

    /// <summary>前回選択していた SF 実行フォルダ（起動時の復元用）。</summary>
    public string? LastFolder { get; set; }

    /// <summary>履歴の種別ごとの最大保持件数。</summary>
    public int MaxHistoryPerType { get; set; } = 2000;

    /// <summary>このバイト数を超える実行結果は results/ 配下の別ファイルに保存する（既定 64KB）。</summary>
    public long ResultInlineThresholdBytes { get; set; } = 64 * 1024;

    /// <summary>sf 実行ファイルのパス（null / 空 = 自動検出）。</summary>
    public string? SfExecutablePath { get; set; }

    /// <summary>Windows Terminal (wt.exe) のパス（null / 空 = 自動検出）。</summary>
    public string? WindowsTerminalPath { get; set; }

    /// <summary>VS Code CLI (code.cmd) のパス（null / 空 = 自動検出）。</summary>
    public string? VsCodePath { get; set; }

    /// <summary>実行前確認ポリシー（ConfirmPolicies の値）。</summary>
    public string ConfirmPolicy { get; set; } = ConfirmPolicies.Dangerous;
}

/// <summary>設定の読み書き（settings.json）。</summary>
public sealed class AppSettingsStore
{
    private readonly string _filePath;
    private readonly AppLog _log;

    public AppSettings Current { get; }

    public event Action? Changed;

    public AppSettingsStore(AppPaths paths, AppLog log)
    {
        _filePath = paths.SettingsFile;
        _log = log;
        Current = AtomicJsonFile.Load<AppSettings>(_filePath, log);
    }

    public void Save()
    {
        AtomicJsonFile.Save(_filePath, Current, _log);
        Changed?.Invoke();
    }
}
