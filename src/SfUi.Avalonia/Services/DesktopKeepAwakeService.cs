using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SfUi.Core;

namespace SfUi.Avalonia.Services;

/// <summary>
/// デスクトップ（Windows / macOS）のアイドル スリープを抑止する実装。
/// Windows は SetThreadExecutionState、macOS は caffeinate 子プロセスを使う。
/// </summary>
public sealed class DesktopKeepAwakeService : IKeepAwakeService
{
    private readonly AppLog _log;
    private readonly object _gate = new();
    private Process? _caffeinate;
    private bool _active;

    public DesktopKeepAwakeService(AppLog log)
    {
        _log = log;
    }

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    public void SetRequired(bool required)
    {
        lock (_gate)
        {
            if (_active == required)
            {
                return;
            }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    SetWindowsExecutionState(required);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    SetMacCaffeinate(required);
                }

                _active = required;
                _log.Info(required ? "スリープ抑止を有効にしました。" : "スリープ抑止を解除しました。");
            }
            catch (Exception ex)
            {
                _log.Warn($"スリープ抑止の切り替えで例外が発生しました: {ex.Message}");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private void SetWindowsExecutionState(bool required)
    {
        var flags = required
            ? ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.AwayModeRequired
            : ExecutionState.Continuous;

        if (SetThreadExecutionState(flags) == 0)
        {
            _log.Warn($"スリープ抑止の設定に失敗しました (required={required}, lastError={Marshal.GetLastWin32Error()})");
        }
    }

    private void SetMacCaffeinate(bool required)
    {
        if (!required)
        {
            try
            {
                if (_caffeinate is { HasExited: false })
                {
                    _caffeinate.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"caffeinate の停止に失敗しました: {ex.Message}");
            }
            finally
            {
                _caffeinate?.Dispose();
                _caffeinate = null;
            }

            return;
        }

        if (_caffeinate is { HasExited: false })
        {
            return;
        }

        var pid = Environment.ProcessId;
        _caffeinate = Process.Start(new ProcessStartInfo
        {
            FileName = "/usr/bin/caffeinate",
            Arguments = $"-i -s -w {pid}",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        AwayModeRequired = 0x00000040,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(ExecutionState esFlags);
}
