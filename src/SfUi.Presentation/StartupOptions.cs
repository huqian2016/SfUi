namespace SfUi.Presentation;

/// <summary>
/// 起動オプション（ようこそ画面などの表示制御用）。値はアプリ起動時にコマンドライン引数から設定し、DI に登録する。
/// </summary>
public sealed class StartupOptions
{
    /// <summary>sf 未検出の UI を確認するための開発フラグ（--welcome-missing）。実行時の sf 検出は行わない。</summary>
    public bool SimulateSfMissing { get; init; }
}
