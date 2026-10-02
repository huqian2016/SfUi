using System.Diagnostics;
using System.Text;

namespace SfUi.Core;

/// <summary>
/// ターミナル / エクスプローラー / VS Code / ブラウザの起動サービス。
/// </summary>
public sealed class ToolLauncherService
{
    private readonly AppLog _log;
    private readonly AppSettingsStore? _settings;

    public ToolLauncherService(AppLog log, AppSettingsStore? settings = null)
    {
        _log = log;
        _settings = settings;
    }

    public sealed record LaunchResult(bool Success, string Message);

    /// <summary>ターミナルを指定フォルダで起動する（kind: wt / powershell / cmd / wsl）。</summary>
    public LaunchResult LaunchTerminal(string folder, string kind = "wt")
    {
        if (!Directory.Exists(folder))
        {
            return new LaunchResult(false, $"フォルダが存在しません: {folder}");
        }

        try
        {
            switch (kind)
            {
                case "powershell":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoExit -Command Set-Location -LiteralPath \"{folder}\"",
                        UseShellExecute = true,
                        WorkingDirectory = folder,
                    });
                    return new LaunchResult(true, $"PowerShell を起動: {folder}");

                case "cmd":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k cd /d {SfCliRunner.QuoteArgument(folder)}",
                        UseShellExecute = true,
                        WorkingDirectory = folder,
                    });
                    return new LaunchResult(true, $"コマンド プロンプトを起動: {folder}");

                case "wsl":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "wsl.exe",
                        Arguments = $"--cd {SfCliRunner.QuoteArgument(ToWslPath(folder))}",
                        UseShellExecute = true,
                        WorkingDirectory = folder,
                    });
                    return new LaunchResult(true, $"WSL を起動: {folder}");

                default:
                    var wtPath = ResolveConfiguredPath(_settings?.Current.WindowsTerminalPath) ?? ResolveWindowsTerminalPath();
                    if (wtPath is not null)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = wtPath,
                            Arguments = $"-d {SfCliRunner.QuoteArgument(folder)}",
                            UseShellExecute = false,
                            WorkingDirectory = folder,
                        });
                        return new LaunchResult(true, $"Windows Terminal を起動: {folder}");
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k cd /d {SfCliRunner.QuoteArgument(folder)}",
                        UseShellExecute = true,
                        WorkingDirectory = folder,
                    });
                    return new LaunchResult(true, $"コマンド プロンプトを起動（Windows Terminal 未検出）: {folder}");
            }
        }
        catch (Exception ex)
        {
            _log.Error("ターミナルの起動に失敗", ex);
            return new LaunchResult(false, $"ターミナルの起動に失敗: {ex.Message}");
        }
    }

    /// <summary>エクスプローラーでフォルダ（またはファイル）を開く。select=true でファイルを選択状態にする。</summary>
    public LaunchResult LaunchExplorer(string path, bool select = false)
    {
        try
        {
            var arguments = select ? "/select," + SfCliRunner.QuoteArgument(path) : SfCliRunner.QuoteArgument(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true,
            });
            return new LaunchResult(true, $"エクスプローラー: {path}");
        }
        catch (Exception ex)
        {
            _log.Error("エクスプローラーの起動に失敗", ex);
            return new LaunchResult(false, $"エクスプローラーの起動に失敗: {ex.Message}");
        }
    }

    /// <summary>VS Code を指定フォルダで起動する（mode: new / reuse / current）。</summary>
    public LaunchResult LaunchVsCode(string path, string mode = "new")
    {
        var codePath = ResolveConfiguredPath(_settings?.Current.VsCodePath) ?? ResolveVsCodeCliPath();
        if (codePath is null)
        {
            return new LaunchResult(false, "VS Code (code.cmd) が見つかりません。");
        }

        try
        {
            var arguments = new List<string>();
            switch (mode)
            {
                case "reuse":
                    arguments.Add("-r");
                    break;
                case "new":
                default:
                    arguments.Add("-n");
                    break;
            }

            arguments.Add(path);

            var commandLine = SfCliRunner.BuildCommandLine(codePath, arguments);
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = SfCliRunner.ToCmdArguments(commandLine),
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.Exists(path) ? path : Environment.CurrentDirectory,
            });
            return new LaunchResult(true, $"VS Code を起動: {path}");
        }
        catch (Exception ex)
        {
            _log.Error("VS Code の起動に失敗", ex);
            return new LaunchResult(false, $"VS Code の起動に失敗: {ex.Message}");
        }
    }

    /// <summary>既定のブラウザで URL を開く。</summary>
    public LaunchResult LaunchBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return new LaunchResult(true, $"ブラウザ: {url}");
        }
        catch (Exception ex)
        {
            _log.Error("ブラウザの起動に失敗", ex);
            return new LaunchResult(false, $"ブラウザの起動に失敗: {ex.Message}");
        }
    }

    /// <summary>設定で指定されたパスが実在すれば使う（null 時は自動検出へフォールバック）。</summary>
    private static string? ResolveConfiguredPath(string? configuredPath)
        => !string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath) ? configuredPath : null;

    /// <summary>Windows Terminal (wt.exe) のパスを解決する（未検出時は null）。</summary>
    public static string? ResolveWindowsTerminalPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var alias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(alias))
        {
            return alias;
        }

        return FindOnPath("wt.exe");
    }

    /// <summary>VS Code CLI (code.cmd) のパスを解決する（未検出時は null）。</summary>
    public static string? ResolveVsCodeCliPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installed = Path.Combine(localAppData, "Programs", "Microsoft VS Code", "bin", "code.cmd");
        if (File.Exists(installed))
        {
            return installed;
        }

        return FindOnPath("code.cmd") ?? FindOnPath("code.exe");
    }

    private static string? FindOnPath(string fileName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // 不正な PATH 要素は無視
            }
        }

        return null;
    }

    /// <summary>Windows パスを WSL 用パスへ変換する（C:\foo\bar → /mnt/c/foo/bar）。</summary>
    public static string ToWslPath(string windowsPath)
    {
        if (string.IsNullOrWhiteSpace(windowsPath) || windowsPath.StartsWith('/'))
        {
            return windowsPath;
        }

        var path = windowsPath.Replace('\\', '/');
        if (path.Length >= 2 && path[1] == ':')
        {
            var drive = char.ToLowerInvariant(path[0]);
            var rest = path.Length > 2 ? path[2..] : string.Empty;
            if (!rest.StartsWith('/'))
            {
                rest = "/" + rest;
            }

            return $"/mnt/{drive}{rest}";
        }

        return path;
    }
}
