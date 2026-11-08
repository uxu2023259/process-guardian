using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ProcessGuardian.Services;

namespace ProcessGuardian.Controls;

public partial class ProcessTargetOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    public ProcessTargetOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeNonInteractive();
    }

    public void UpdatePreview(DesktopWindowPickerPreview preview)
    {
        var target = preview.Target;
        DesktopWindowBounds bounds;
        if (target is not null)
        {
            bounds = target.Bounds;
            var error = target.ErrorMessage;
            var isLoading = target.IsLoading;
            var hasPath = !string.IsNullOrWhiteSpace(target.ExecutablePath);
            var status = error ?? (isLoading
                ? "正在读取程序路径…"
                : hasPath ? "松开鼠标以接管此进程 · Esc 取消" : "无法读取程序路径。");
            TargetOverlay.State = new ProcessTargetDisplayState(
                string.IsNullOrWhiteSpace(target.WindowTitle) ? "目标窗口" : target.WindowTitle,
                string.IsNullOrWhiteSpace(target.ProcessName) ? null : target.ProcessName,
                target.ProcessId,
                target.ExecutablePath,
                status,
                isLoading,
                ShowHighlight: true,
                IsError: !isLoading && !hasPath || error is not null);
        }
        else
        {
            bounds = GetPromptBounds(preview.Cursor);
            TargetOverlay.State = new ProcessTargetDisplayState(
                "拖动到目标程序窗口", null, null, null,
                preview.WaitingForMouseDown
                    ? "请在目标窗口内按住鼠标左键并拖动。"
                    : "将准星拖到要保护的程序窗口，再松开鼠标。按 Esc 取消。",
                IsLoading: false,
                ShowHighlight: false,
                IsError: false);
        }

        var dpi = GetMonitorDpi(bounds.Left, bounds.Top);
        Width = Math.Max(1, bounds.Width * 96d / dpi.X);
        Height = Math.Max(1, bounds.Height * 96d / dpi.Y);

        if (!IsVisible) Show();
        SetNativeBounds(bounds);

        if (target is not null)
        {
            TargetOverlay.SetPointerPosition(
                (preview.Cursor.X - bounds.Left) * 96d / dpi.X,
                (preview.Cursor.Y - bounds.Top) * 96d / dpi.Y);
        }
    }

    private void MakeNonInteractive()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var existing = GetExtendedStyle(handle);
        var style = existing | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate;
        _ = SetExtendedStyle(handle, style);
    }

    private void SetNativeBounds(DesktopWindowBounds bounds)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _ = SetWindowPos(handle, new nint(-1), bounds.Left, bounds.Top,
            bounds.Width, bounds.Height, SwpNoActivate | SwpNoOwnerZOrder | SwpShowWindow);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible) return;
            _ = SetWindowPos(handle, new nint(-1), bounds.Left, bounds.Top,
                bounds.Width, bounds.Height, SwpNoActivate | SwpNoOwnerZOrder | SwpShowWindow);
        }), DispatcherPriority.Background);
    }

    private static DesktopWindowBounds GetPromptBounds(DesktopWindowPoint cursor)
    {
        const int width = 340;
        const int height = 92;
        var monitor = MonitorFromPoint(new NativePoint(cursor.X, cursor.Y), MonitorDefaultToNearest);
        if (monitor == 0) return new DesktopWindowBounds(cursor.X + 18, cursor.Y + 18, width, height);

        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return new DesktopWindowBounds(cursor.X + 18, cursor.Y + 18, width, height);

        var workArea = info.WorkArea;
        var left = cursor.X + 18;
        var top = cursor.Y + 18;
        if (left + width > workArea.Right) left = cursor.X - width - 18;
        if (top + height > workArea.Bottom) top = cursor.Y - height - 18;
        left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
        return new DesktopWindowBounds(left, top, width, height);
    }

    private static (uint X, uint Y) GetMonitorDpi(int x, int y)
    {
        var monitor = MonitorFromPoint(new NativePoint(x, y), MonitorDefaultToNearest);
        if (monitor != 0 && GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out var dpiY) == 0 &&
            dpiX > 0 && dpiY > 0)
            return (dpiX, dpiY);
        return (96, 96);
    }

    private static long GetExtendedStyle(nint handle) => IntPtr.Size == 8
        ? GetWindowLongPtr64(handle, GwlExStyle).ToInt64()
        : GetWindowLong32(handle, GwlExStyle);

    private static nint SetExtendedStyle(nint handle, long style) => IntPtr.Size == 8
        ? SetWindowLongPtr64(handle, GwlExStyle, new nint(style))
        : new nint(SetWindowLong32(handle, GwlExStyle, unchecked((int)style)));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; public NativePoint(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public uint Size; public NativeRect Monitor; public NativeRect WorkArea; public uint Flags; }

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y,
        int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint handle, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint handle, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("shcore.dll", SetLastError = true)]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
