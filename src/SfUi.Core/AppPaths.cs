namespace SfUi.Core;

/// <summary>
/// データ保存ルートと配下ディレクトリを解決・管理する。
/// </summary>
public sealed class AppPaths
{
    private const int MaxUpwardSearchDepth = 8;

    /// <summary>データルート（例: c:\huqian\vscode\SfUi\data）</summary>
    public string DataRoot { get; }

    public string LogsDirectory { get; }
    public string HistoryDirectory { get; }
    public string ResultsDirectory { get; }
    public string TempDirectory { get; }

    /// <summary>settings.json のフルパス</summary>
    public string SettingsFile { get; }

    /// <summary>ETL 実行ディレクトリ（data/etl/runs）。実行ごとにサブフォルダーを作る。</summary>
    public string EtlRunsRoot { get; }

    /// <summary>ETL ジョブ状態ディレクトリ（data/etl/jobs）。delta の watermark 等を保持し、run 削除の影響を受けない。</summary>
    public string EtlJobsRoot { get; }

    /// <summary>スケジューラのルート（data/scheduler）。</summary>
    public string SchedulerRoot { get; }

    /// <summary>スケジュール済みジョブ定義ファイル（data/scheduler/jobs.json）。</summary>
    public string SchedulerJobsFile { get; }

    /// <summary>ジョブ実行履歴のルート（data/scheduler/runs/&lt;yyyy-MM&gt;）。</summary>
    public string SchedulerRunsRoot { get; }

    /// <summary>スケジューラの二重起動防止ロック ファイル（data/scheduler/scheduler.lock）。</summary>
    public string SchedulerLockFile { get; }

    /// <summary>予約実行ジョブの出力先の既定フォルダ（data/exports）。相対パスはここからの相対として解決する。</summary>
    public string ExportsDirectory { get; }

    /// <summary>ローカル レポートのルート（data/reports）。</summary>
    public string ReportsRoot { get; }

    /// <summary>レポート定義ファイル（data/reports/reports.json）。</summary>
    public string ReportsFile { get; }

    /// <summary>帳票レイアウト定義ファイル（data/reports/layouts.json・将来の SVF 風帳票用）。</summary>
    public string ReportLayoutsFile { get; }

    private AppPaths(string dataRoot)
    {
        DataRoot = dataRoot;
        LogsDirectory = Path.Combine(dataRoot, "logs");
        HistoryDirectory = Path.Combine(dataRoot, "history");
        ResultsDirectory = Path.Combine(dataRoot, "results");
        TempDirectory = Path.Combine(dataRoot, "tmp");
        SettingsFile = Path.Combine(dataRoot, "settings.json");
        EtlRunsRoot = Path.Combine(dataRoot, "etl", "runs");
        EtlJobsRoot = Path.Combine(dataRoot, "etl", "jobs");
        SchedulerRoot = Path.Combine(dataRoot, "scheduler");
        SchedulerJobsFile = Path.Combine(SchedulerRoot, "jobs.json");
        SchedulerRunsRoot = Path.Combine(SchedulerRoot, "runs");
        SchedulerLockFile = Path.Combine(SchedulerRoot, "scheduler.lock");
        ExportsDirectory = Path.Combine(dataRoot, "exports");
        ReportsRoot = Path.Combine(dataRoot, "reports");
        ReportsFile = Path.Combine(ReportsRoot, "reports.json");
        ReportLayoutsFile = Path.Combine(ReportsRoot, "layouts.json");

        foreach (var directory in new[] { DataRoot, LogsDirectory, HistoryDirectory, ResultsDirectory, TempDirectory })
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// データルートを決定する。優先順位:
    /// 1) 明示指定（--data-dir / SFUI_DATA_DIR）
    /// 2) 開発時: ソリューションルート（SfUi.sln を上方探索）の data/
    /// 3) ポータブル: 実行ファイル隣の data/（書込可の場合）
    /// 4) OS 既定（Windows: %APPDATA%\SfUi / macOS: ~/Library/Application Support/SfUi）
    /// </summary>
    public static AppPaths Resolve(string? dataRootOverride = null, string? baseDirectory = null, string? appDataDirectory = null)
    {
        baseDirectory ??= AppContext.BaseDirectory;
        appDataDirectory ??= ResolveDefaultAppDataDirectory();

        if (!string.IsNullOrWhiteSpace(dataRootOverride))
        {
            return new AppPaths(Path.GetFullPath(dataRootOverride));
        }

        var solutionRoot = FindSolutionRoot(baseDirectory);
        if (solutionRoot is not null)
        {
            return new AppPaths(Path.Combine(solutionRoot, "data"));
        }

        var portableRoot = Path.Combine(Path.GetFullPath(baseDirectory), "data");
        if (IsWritable(portableRoot))
        {
            return new AppPaths(portableRoot);
        }

        return new AppPaths(Path.Combine(appDataDirectory, "SfUi"));
    }

    /// <summary>
    /// OS 既定の設定ディレクトリ。macOS は .NET の ApplicationData（~/.config）ではなく
    /// macOS 慣習の ~/Library/Application Support を明示的に使う。
    /// </summary>
    private static string ResolveDefaultAppDataDirectory()
    {
        if (PlatformInfo.IsMacOS)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support");
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    private static string? FindSolutionRoot(string baseDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(baseDirectory));
        for (var depth = 0; directory is not null && depth < MaxUpwardSearchDepth; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SfUi.sln")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probeFile = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probeFile, string.Empty);
            File.Delete(probeFile);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
