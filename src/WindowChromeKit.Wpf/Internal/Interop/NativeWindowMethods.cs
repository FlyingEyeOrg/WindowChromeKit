using System.Runtime.InteropServices;

namespace WindowChromeKit.Wpf.Internal;

internal static class NativeWindowMethods
{
    internal const uint MonitorDefaultToPrimary = 0x00000001;
    internal const uint MonitorDefaultToNearest = 0x00000002;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const uint SwpFrameChanged = 0x0020;
    internal const uint SwpShowWindow = 0x0040;
    internal const int ShowWindowHide = 0;
    internal const int GwlStyle = -16;
    internal const int SmCxFrame = 32;
    internal const int SmCyFrame = 33;
    internal const int SmCxMinTrack = 34;
    internal const int SmCxPaddedBorder = 92;
    internal const int SmXVirtualScreen = 76;
    internal const int SmYVirtualScreen = 77;
    internal const int SmCxVirtualScreen = 78;
    internal const int SmCyVirtualScreen = 79;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr WindowProcedure(
        IntPtr windowHandle,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnableWindow(
        IntPtr windowHandle,
        [MarshalAs(UnmanagedType.Bool)] bool enabled);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowEnabled(IntPtr windowHandle);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetLastActivePopup(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetCapture(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitorHandle, ref NativeMonitorInfo monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr windowHandle, out NativeRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(IntPtr windowHandle, out NativeRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(IntPtr windowHandle, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(IntPtr windowHandle);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static extern IntPtr SendMessage(IntPtr windowHandle, uint message, IntPtr wordParameter, IntPtr longParameter);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "RegisterClassExW")]
    internal static extern ushort RegisterWindowClass(
        [In, Out] ref NativeWindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
    internal static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr owner,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static extern IntPtr DefWindowProc(
        IntPtr windowHandle,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TrackMouseEvent(ref NativeTrackMouseEvent trackMouseEvent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("gdi32.dll", SetLastError = true, EntryPoint = "CreateRectRgn")]
    internal static extern IntPtr CreateRectRegion(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", EntryPoint = "CombineRgn")]
    internal static extern int CombineRegion(IntPtr destination, IntPtr source1, IntPtr source2, int mode);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowRgn")]
    internal static extern int SetWindowRegion(
        IntPtr windowHandle,
        IntPtr region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr value);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(
        IntPtr monitorHandle,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmExtendFrameIntoClientArea(
        IntPtr windowHandle,
        ref NativeMargins margins);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    internal static extern IntPtr GetModuleHandle(string? moduleName);
}
