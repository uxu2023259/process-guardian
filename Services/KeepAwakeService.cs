using System.ComponentModel;
using System.Runtime.InteropServices;
using ProcessGuardian.Models;

namespace ProcessGuardian.Services;

public sealed class KeepAwakeService
{
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;
    private const uint EsContinuous = 0x80000000;
    private const uint SpiGetScreenSaveActive = 0x0010;
    private const uint SpiSetScreenSaveActive = 0x0011;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint executionState);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr value, uint flags);

    public void Enable(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ScreenSaverWasActive ??= GetScreenSaverActive();

        if (SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法请求系统保持唤醒。");

        try
        {
            SetScreenSaverActive(false);
        }
        catch
        {
            SetThreadExecutionState(EsContinuous);
            throw;
        }
    }

    public void Disable(AppSettings settings, bool clearRestoreState = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Exception? restoreError = null;

        if (settings.ScreenSaverWasActive is bool previousState)
        {
            try { SetScreenSaverActive(previousState); }
            catch (Exception exception) { restoreError = exception; }
        }

        var executionStateCleared = SetThreadExecutionState(EsContinuous) != 0;
        if (!executionStateCleared && restoreError is null)
            restoreError = new Win32Exception(Marshal.GetLastWin32Error(), "无法恢复系统电源管理状态。");

        if (restoreError is not null) throw restoreError;
        if (clearRestoreState) settings.ScreenSaverWasActive = null;
    }

    private static bool GetScreenSaverActive()
    {
        if (!SystemParametersInfo(SpiGetScreenSaveActive, 0, out var active, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取屏幕保护程序状态。");
        return active;
    }

    private static void SetScreenSaverActive(bool active)
    {
        if (!SystemParametersInfo(SpiSetScreenSaveActive, active ? 1u : 0u, IntPtr.Zero,
                SpifUpdateIniFile | SpifSendChange))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法更新屏幕保护程序状态。");
    }
}
