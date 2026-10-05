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

    /// <summary>UI 言語（"en" = 英語／既定、"ja" = 日本語）。</summary>
    public string Language { get; set; } = UiText.English;

    /// <summary>AI パネル（右サイド）の表示状態。</summary>
    public bool AiPanelVisible { get; set; } = true;

    /// <summary>AI チャットの接続先エンドポイント（null / 空 = DeepSeek 既定。OpenAI 互換 API を指定可能）。</summary>
    public string? AiEndpoint { get; set; }

    /// <summary>AI チャットの API キー（null / 空 = 環境変数 SFUI_AI_API_KEY → 内蔵キー〔DeepSeek 既定接続先のみ〕）。</summary>
    public string? AiApiKey { get; set; }

    /// <summary>AI チャットのモデル名（null / 空 = 既定: deepseek-chat）。</summary>
    public string? AiModel { get; set; }

    /// <summary>旧名の API キー（AiApiKey へ移行済み。読み取り互換用）。</summary>
    public string? DeepSeekApiKey { get; set; }

    /// <summary>旧名のモデル名（AiModel へ移行済み。読み取り互換用）。</summary>
    public string? DeepSeekModel { get; set; }

    /// <summary>実行前確認ポリシー（ConfirmPolicies の値）。</summary>
    public string ConfirmPolicy { get; set; } = ConfirmPolicies.Dangerous;

    /// <summary>バックアップで REST（JSON）を使うレコード数の上限。超えるオブジェクトは Bulk API（CSV）を使う。</summary>
    public int BackupRestMaxRecords { get; set; } = 2000;

    /// <summary>ようこそ画面を「今後表示しない」にしたか（true の間は起動時に表示しない。設定タブから再表示可能）。</summary>
    public bool WelcomeDismissed { get; set; }

    /// <summary>SOQL 実行後に AI パネルへ自動送信するか（エラー時 = 修正方法、成功時 = 次の候補）。</summary>
    public bool SoqlAiAssist { get; set; } = true;
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
        MigrateLegacyAiSettings(Current);
    }

    /// <summary>旧フィールド（DeepSeekApiKey / DeepSeekModel）の値を汎用フィールド（AiApiKey / AiModel）へ引き継ぐ。</summary>
    private static void MigrateLegacyAiSettings(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AiApiKey) && !string.IsNullOrWhiteSpace(settings.DeepSeekApiKey))
        {
            settings.AiApiKey = settings.DeepSeekApiKey;
        }

        if (string.IsNullOrWhiteSpace(settings.AiModel) && !string.IsNullOrWhiteSpace(settings.DeepSeekModel))
        {
            settings.AiModel = settings.DeepSeekModel;
        }

        settings.DeepSeekApiKey = null;
        settings.DeepSeekModel = null;
    }

    public void Save()
    {
        AtomicJsonFile.Save(_filePath, Current, _log);
        Changed?.Invoke();
    }
}
