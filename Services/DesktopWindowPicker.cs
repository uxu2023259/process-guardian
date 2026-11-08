using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace ProcessGuardian.Services;

public sealed record DesktopWindowBounds(int Left, int Top, int Width, int Height);

public sealed record DesktopWindowPoint(int X, int Y);

public sealed record DesktopWindowTarget(
    nint WindowHandle,
    int ProcessId,
    string WindowTitle,
    string ProcessName,
    string? ExecutablePath,
    DesktopWindowBounds Bounds,
    bool IsLoading,
    string? ErrorMessage,
    long? ProcessStartTimeUtcTicks = null);

public sealed record DesktopWindowPickerPreview(
    DesktopWindowTarget? Target,
    DesktopWindowPoint Cursor,
    bool WaitingForMouseDown,
    bool IsDragging);

public sealed record DesktopWindowPickResult(
    DesktopWindowTarget? Target,
    bool Canceled,
    string? Message);

public sealed class DesktopWindowPicker : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WhKeyboardLl = 13;
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmRightButtonDown = 0x0204;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const uint VkEscape = 0x1B;
    private const uint GaRoot = 2;
    private const uint WmGetText = 0x000D;
    private const uint SmtoBlock = 0x0001;
    private const uint SmtoAbortIfHung = 0x0002;

    private readonly Dispatcher _dispatcher;
    private readonly HookProcedure _mouseProcedure;
    private readonly HookProcedure _keyboardProcedure;
    private nint _mouseHook;
    private nint _keyboardHook;
    private TaskCompletionSource<DesktopWindowPickResult>? _completion;
    private WindowCandidate? _candidate;
    private DesktopWindowTarget? _target;
    private DesktopWindowPoint _startPoint = new(0, 0);
    private DesktopWindowPoint _lastPoint = new(0, 0);
    private long _lastPreviewTicks;
    private int _generation;
    private bool _active;
    private bool _waitingForMouseDown;
    private bool _dragging;
    private bool _hasMoved;
    private bool _disposed;

    public DesktopWindowPicker(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _mouseProcedure = MouseHookCallback;
        _keyboardProcedure = KeyboardHookCallback;
    }

    public event Action<DesktopWindowPickerPreview>? PreviewChanged;
    public event Action? DragStarted;

    public Task<DesktopWindowPickResult> Start(bool waitingForMouseDown)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_active) throw new InvalidOperationException("窗口选择已经开始。");

        _completion = new TaskCompletionSource<DesktopWindowPickResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _active = true;
        _waitingForMouseDown = waitingForMouseDown;
        _dragging = !waitingForMouseDown;
        _hasMoved = false;
        _candidate = null;
        _target = null;
        _generation = 0;
        _lastPreviewTicks = 0;
        if (GetCursorPos(out var cursor))
            _startPoint = _lastPoint = new DesktopWindowPoint(cursor.X, cursor.Y);

        var module = GetModuleHandle(null);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProcedure, module, 0);
        if (_mouseHook == 0)
        {
            _active = false;
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法启动全局鼠标跟踪。");
        }

        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProcedure, module, 0);
        if (_keyboardHook == 0)
        {
            var error = Marshal.GetLastWin32Error();
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
            _active = false;
            throw new Win32Exception(error, "无法启用 Esc 取消键盘跟踪。");
        }

        PublishPreview(new DesktopWindowPickerPreview(null, _lastPoint,
            _waitingForMouseDown, _dragging));
        return _completion.Task;
    }

    public void Cancel()
    {
        if (!_active) return;
        Finish(new DesktopWindowPickResult(null, Canceled: true, "选择已取消。"));
    }

    private nint MouseHookCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0 || !_active)
            return CallNextHookEx(_mouseHook, code, wParam, lParam);

        try
        {
            var message = unchecked((int)wParam);
            var mouse = Marshal.PtrToStructure<MouseHookData>(lParam);
            var point = new DesktopWindowPoint(mouse.Point.X, mouse.Point.Y);

            if (_waitingForMouseDown && message == WmLeftButtonDown)
            {
                _waitingForMouseDown = false;
                _dragging = true;
                _hasMoved = false;
                _startPoint = _lastPoint = point;
                _dispatcher.BeginInvoke(new Action(() => DragStarted?.Invoke()), DispatcherPriority.Send);
                PublishPreview(new DesktopWindowPickerPreview(null, point, false, true));
                return 1;
            }

            if (_dragging && message == WmMouseMove)
            {
                OnMouseMove(point);
            }
            else if (_dragging && message == WmLeftButtonUp)
            {
                var moved = _hasMoved || IsPastDragThreshold(point, _startPoint);
                var candidate = moved ? FindWindowAt(point) : null;
                var resultMessage = moved
                    ? candidate is null ? "未选中可接管的应用窗口，请拖到其他程序窗口后松开。" : null
                    : "请按住准星拖到目标窗口后再松开。";
                FinishFromHook(candidate, canceled: false, resultMessage);
                return 1;
            }
            else if ((_dragging || _waitingForMouseDown) && message == WmRightButtonDown)
            {
                FinishFromHook(null, canceled: true, "选择已取消。");
                return 1;
            }
        }
        catch
        {
            // Keep the low-level callback short and always let unrelated input continue.
            if (unchecked((int)wParam) == WmLeftButtonUp && _dragging)
            {
                FinishFromHook(null, canceled: false, "无法识别目标窗口，请重试。");
                return 1;
            }
        }

        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private nint KeyboardHookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _active &&
            (unchecked((int)wParam) is WmKeyDown or WmSysKeyDown) &&
            Marshal.PtrToStructure<KeyboardHookData>(lParam).VirtualKeyCode == VkEscape)
        {
            FinishFromHook(null, canceled: true, "选择已取消。");
            return 1;
        }

        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void OnMouseMove(DesktopWindowPoint point)
    {
        _lastPoint = point;
        if (!_hasMoved && !IsPastDragThreshold(point, _startPoint)) return;
        _hasMoved = true;

        var now = Stopwatch.GetTimestamp();
        if (now - _lastPreviewTicks < Stopwatch.Frequency / 30) return;
        _lastPreviewTicks = now;

        var candidate = FindWindowAt(point);
        if (!SameCandidate(_candidate, candidate))
        {
            _candidate = candidate;
            var generation = ++_generation;
            _target = candidate is null ? null : new DesktopWindowTarget(candidate.Handle,
                candidate.ProcessId, candidate.WindowTitle, "", null, candidate.Bounds,
                IsLoading: true, ErrorMessage: null, ProcessStartTimeUtcTicks: null);
            if (candidate is not null)
                ResolveForPreview(candidate, generation);
        }

        PublishPreview(new DesktopWindowPickerPreview(_target, point, false, true));
    }

    private void ResolveForPreview(WindowCandidate candidate, int generation)
    {
        _ = Task.Run(() => ReadTarget(candidate)).ContinueWith(task =>
        {
            var resolved = task.Status == TaskStatus.RanToCompletion
                ? task.Result
                : new DesktopWindowTarget(candidate.Handle, candidate.ProcessId,
                    candidate.WindowTitle, "", null, candidate.Bounds, IsLoading: false,
                    ErrorMessage: "无法读取所选程序信息。");

            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_active || !_dragging || generation != _generation) return;
                _target = resolved;
                PublishPreview(new DesktopWindowPickerPreview(resolved, _lastPoint, false, true));
            }), DispatcherPriority.Background);
        }, TaskScheduler.Default);
    }

    private void FinishFromHook(WindowCandidate? candidate, bool canceled, string? message)
    {
        if (!_active) return;
        _active = false;
        _waitingForMouseDown = false;
        _dragging = false;
        var completion = _completion;
        _dispatcher.BeginInvoke(new Action(async () =>
        {
            Unhook();
            if (canceled || candidate is null)
            {
                completion?.TrySetResult(new DesktopWindowPickResult(null, canceled, message));
                return;
            }

            var target = await Task.Run(() => ReadTarget(candidate));
            completion?.TrySetResult(new DesktopWindowPickResult(target, Canceled: false,
                target.ErrorMessage));
        }), DispatcherPriority.Background);
    }

    private void Finish(DesktopWindowPickResult result)
    {
        if (!_active) return;
        _active = false;
        _waitingForMouseDown = false;
        _dragging = false;
        Unhook();
        _completion?.TrySetResult(result);
    }

    private void Unhook()
    {
        if (_mouseHook != 0)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
        if (_keyboardHook != 0)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
    }

    private void PublishPreview(DesktopWindowPickerPreview preview)
    {
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (_active) PreviewChanged?.Invoke(preview);
        }), DispatcherPriority.Background);
    }

    private static WindowCandidate? FindWindowAt(DesktopWindowPoint point)
    {
        var handle = WindowFromPoint(new NativePoint(point.X, point.Y));
        if (handle == 0) return null;
        var root = GetAncestor(handle, GaRoot);
        if (root == 0) root = handle;
        if (!IsWindow(root) || !IsWindowVisible(root)) return null;

        _ = GetWindowThreadProcessId(root, out var processId);
        if (processId == 0 || processId > int.MaxValue || processId == (uint)Environment.ProcessId) return null;

        var className = new StringBuilder(128);
        _ = GetClassName(root, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or
            "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "#32769")
            return null;

        if (!GetWindowRect(root, out var rect)) return null;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 40 || height < 30) return null;

        return new WindowCandidate(root, (int)processId, "",
            new DesktopWindowBounds(rect.Left, rect.Top, width, height));
    }

    private static DesktopWindowTarget ReadTarget(WindowCandidate candidate)
    {
        try
        {
            if (!IsWindow(candidate.Handle) || !IsWindowVisible(candidate.Handle))
                return Failed(candidate, "目标窗口已关闭。");
            _ = GetWindowThreadProcessId(candidate.Handle, out var currentProcessId);
            if (currentProcessId != (uint)candidate.ProcessId)
                return Failed(candidate, "目标窗口对应的程序已变化。");

            var windowTitle = ReadWindowTitle(candidate.Handle);
            candidate = candidate with { WindowTitle = windowTitle };

            using var process = Process.GetProcessById(candidate.ProcessId);
            if (process.HasExited) return Failed(candidate, "目标程序已退出。");

            var processName = process.ProcessName;
            if (string.Equals(processName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                return Failed(candidate,
                    "无法安全识别此 Windows 打包应用的实际进程，请改选传统桌面程序窗口。",
                    processName);

            var processStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            var executablePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
                return Failed(candidate, "无法读取程序路径，可能是权限不足。", processName);

            executablePath = Path.GetFullPath(executablePath);
            if (!File.Exists(executablePath))
                return Failed(candidate, "程序文件不存在，无法在退出后重新启动。", processName, executablePath);

            return new DesktopWindowTarget(candidate.Handle, candidate.ProcessId,
                string.IsNullOrWhiteSpace(candidate.WindowTitle) ? processName : candidate.WindowTitle,
                processName, executablePath, candidate.Bounds, IsLoading: false, ErrorMessage: null,
                ProcessStartTimeUtcTicks: processStartTimeUtcTicks);
        }
        catch (Win32Exception)
        {
            return Failed(candidate, "无法读取程序路径，可能是权限不足。");
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(candidate, "无法读取程序路径，可能是权限不足。");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        {
            return Failed(candidate, "目标程序已退出或路径不可用。");
        }
    }

    private static DesktopWindowTarget Failed(WindowCandidate candidate, string message,
        string processName = "", string? executablePath = null) =>
        new(candidate.Handle, candidate.ProcessId, candidate.WindowTitle, processName,
            executablePath, candidate.Bounds, IsLoading: false, ErrorMessage: message);

    private static string ReadWindowTitle(nint handle)
    {
        var buffer = new StringBuilder(512);
        _ = SendMessageTimeout(handle, WmGetText, (nuint)buffer.Capacity, buffer,
            SmtoBlock | SmtoAbortIfHung, 120, out _);
        return buffer.ToString();
    }

    private static bool SameCandidate(WindowCandidate? left, WindowCandidate? right) =>
        left?.Handle == right?.Handle && left?.ProcessId == right?.ProcessId &&
        Equals(left?.Bounds, right?.Bounds);

    private static bool IsPastDragThreshold(DesktopWindowPoint point, DesktopWindowPoint start)
    {
        var x = (long)point.X - start.X;
        var y = (long)point.Y - start.Y;
        return x * x + y * y >= 64;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cancel();
        Unhook();
    }

    private sealed record WindowCandidate(
        nint Handle,
        int ProcessId,
        string WindowTitle,
        DesktopWindowBounds Bounds);

    private delegate nint HookProcedure(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; public NativePoint(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData { public NativePoint Point; public uint MouseData; public uint Flags; public uint Time; public nuint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData { public uint VirtualKeyCode; public uint ScanCode; public uint Flags; public uint Time; public nuint ExtraInfo; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProcedure callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint handle, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(nint handle, StringBuilder className, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint handle, uint message, nuint wParam,
        StringBuilder lParam, uint flags, uint timeout, out nuint result);
}
