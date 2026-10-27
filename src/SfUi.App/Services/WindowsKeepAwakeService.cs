using System.Runtime.InteropServices;
using SfUi.Core;

namespace SfUi.App.Services;

/// <summary>
/// Windows のアイドル スリープを抑止する実装。
/// ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED を設定している間は
/// システムはスリープしない（画面は消灯できる）。解除は ES_CONTINUOUS のみで行う。
/// </summary>
public sealed class WindowsKeepAwakeService : IKeepAwakeService
{
    private readonly AppLog _log;
    private readonly object _gate = new();
    private bool _active;

    public WindowsKeepAwakeService(AppLog log)
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
                var flags = required
                    ? ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.AwayModeRequired
                    : ExecutionState.Continuous;

                var result = SetThreadExecutionState(flags);
                if (result == 0)
                {
                    _log.Warn($"スリープ抑止の設定に失敗しました (required={required}, lastError={Marshal.GetLastWin32Error()})");
                    return;
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
