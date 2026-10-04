using System.Diagnostics;
using System.Text;

namespace SfUi.Core;

/// <summary>
/// ターミナル / エクスプローラー（macOS: Finder） / VS Code / ブラウザの起動サービス。
/// OS 差はこのクラス内に閉じ込める（Windows / macOS）。
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

    /// <summary>ターミナルを指定フォルダで起動する（kind: wt / powershell / cmd / wsl。macOS は Terminal.app）。</summary>
    public LaunchResult LaunchTerminal(string folder, string kind = "wt")
    {
        if (!Directory.Exists(folder))
        {
            return new LaunchResult(false, UiText.T("Msg_FolderNotExistFmt", folder));
        }

        try
        {
            return PlatformInfo.IsWindows ? LaunchTerminalWindows(folder, kind) : LaunchTerminalMac(folder, kind);
        }
        catch (Exception ex)
        {
            _log.Error("ターミナルの起動に失敗", ex);
            return new LaunchResult(false, UiText.T("Launch_TerminalFailedFmt", ex.Message));
        }
    }

    /// <summary>Windows: wt / powershell / cmd / wsl（従来動作）。</summary>
    private LaunchResult LaunchTerminalWindows(string folder, string kind)
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
                return new LaunchResult(true, UiText.T("Launch_PsFmt", folder));

            case "cmd":
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k cd /d {SfCliRunner.QuoteArgument(folder)}",
                    UseShellExecute = true,
                    WorkingDirectory = folder,
                });
                return new LaunchResult(true, UiText.T("Launch_CmdFmt", folder));

            case "wsl":
                Process.Start(new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = $"--cd {SfCliRunner.QuoteArgument(ToWslPath(folder))}",
                    UseShellExecute = true,
                    WorkingDirectory = folder,
                });
                return new LaunchResult(true, UiText.T("Launch_WslFmt", folder));

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
                    return new LaunchResult(true, UiText.T("Launch_WtFmt", folder));
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k cd /d {SfCliRunner.QuoteArgument(folder)}",
                    UseShellExecute = true,
                    WorkingDirectory = folder,
                });
                return new LaunchResult(true, UiText.T("Launch_CmdFallbackFmt", folder));
        }
    }

    /// <summary>macOS: Terminal.app をフォルダで開く（powershell / cmd / wsl は非対応）。</summary>
    private LaunchResult LaunchTerminalMac(string folder, string kind)
    {
        if (kind is "powershell" or "cmd" or "wsl")
        {
            return new LaunchResult(false, UiText.T("Launch_UnsupportedFmt", kind));
        }

        var startInfo = new ProcessStartInfo { FileName = "open", UseShellExecute = false };
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add("Terminal");
        startInfo.ArgumentList.Add(folder);
        Process.Start(startInfo);
        return new LaunchResult(true, UiText.T("Launch_TerminalFmt", folder));
    }

    /// <summary>フォルダ（またはファイル）を開く（Windows: エクスプローラー / macOS: Finder）。select=true でファイルを選択状態にする。</summary>
    public LaunchResult LaunchExplorer(string path, bool select = false)
    {
        try
        {
            if (PlatformInfo.IsWindows)
            {
                var arguments = select ? "/select," + SfCliRunner.QuoteArgument(path) : SfCliRunner.QuoteArgument(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = arguments,
                    UseShellExecute = true,
                });
            }
            else
            {
                // macOS: open（select=true は -R で Finder に表示）
                var startInfo = new ProcessStartInfo { FileName = "open", UseShellExecute = false };
                if (select)
                {
                    startInfo.ArgumentList.Add("-R");
                }

                startInfo.ArgumentList.Add(path);
                Process.Start(startInfo);
            }

            return new LaunchResult(true, UiText.T("Launch_ExplorerFmt", path));
        }
        catch (Exception ex)
        {
            _log.Error("エクスプローラーの起動に失敗", ex);
            return new LaunchResult(false, UiText.T("Launch_ExplorerFailedFmt", ex.Message));
        }
    }

    /// <summary>VS Code を指定フォルダで起動する（mode: new / reuse / current）。</summary>
    public LaunchResult LaunchVsCode(string path, string mode = "new")
    {
        var codePath = ResolveConfiguredPath(_settings?.Current.VsCodePath) ?? ResolveVsCodeCliPath();
        if (codePath is null)
        {
            return new LaunchResult(false, UiText.T("Launch_VsCodeNotFound"));
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

            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.Exists(path) ? path : Environment.CurrentDirectory,
            };
            if (PlatformInfo.IsWindows)
            {
                startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                startInfo.Arguments = SfCliRunner.ToCmdArguments(SfCliRunner.BuildCommandLine(codePath, arguments));
            }
            else
            {
                startInfo.FileName = codePath;
                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }
            }

            Process.Start(startInfo);
            return new LaunchResult(true, UiText.T("Launch_VsCodeFmt", path));
        }
        catch (Exception ex)
        {
            _log.Error("VS Code の起動に失敗", ex);
            return new LaunchResult(false, UiText.T("Launch_VsCodeFailedFmt", ex.Message));
        }
    }

    /// <summary>既定のブラウザで URL を開く（Windows: UseShellExecute / macOS: open）。</summary>
    public LaunchResult LaunchBrowser(string url)
    {
        try
        {
            if (PlatformInfo.IsWindows)
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            else
            {
                var startInfo = new ProcessStartInfo { FileName = "open", UseShellExecute = false };
                startInfo.ArgumentList.Add(url);
                Process.Start(startInfo);
            }

            return new LaunchResult(true, UiText.T("Launch_BrowserFmt", url));
        }
        catch (Exception ex)
        {
            _log.Error("ブラウザの起動に失敗", ex);
            return new LaunchResult(false, UiText.T("Launch_BrowserFailedFmt", ex.Message));
        }
    }

    /// <summary>設定で指定されたパスが実在すれば使う（null 時は自動検出へフォールバック）。</summary>
    private static string? ResolveConfiguredPath(string? configuredPath)
        => !string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath) ? configuredPath : null;

    /// <summary>Windows Terminal (wt.exe) のパスを解決する（Windows 以外・未検出時は null）。</summary>
    public static string? ResolveWindowsTerminalPath()
    {
        if (!PlatformInfo.IsWindows)
        {
            return null;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var alias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(alias))
        {
            return alias;
        }

        return FindOnPath("wt.exe");
    }

    /// <summary>VS Code CLI のパスを解決する（Windows: code.cmd / macOS: code。未検出時は null）。</summary>
    public static string? ResolveVsCodeCliPath()
    {
        if (PlatformInfo.IsWindows)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var installed = Path.Combine(localAppData, "Programs", "Microsoft VS Code", "bin", "code.cmd");
            if (File.Exists(installed))
            {
                return installed;
            }

            return FindOnPath("code.cmd") ?? FindOnPath("code.exe");
        }

        // macOS: Homebrew / システムの code、VS Code.app 内の CLI
        foreach (var candidate in new[]
        {
            "/opt/homebrew/bin/code",
            "/usr/local/bin/code",
            "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code",
        })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return FindOnPath("code");
    }

    private static string? FindOnPath(string fileName) =>
        PathSearch.Find(Environment.GetEnvironmentVariable("PATH"), new[] { fileName }, PlatformInfo.PathListSeparator);

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
