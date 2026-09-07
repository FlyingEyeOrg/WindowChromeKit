using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>按 Owner、当前窗口或前台窗口选择目标显示器，并读取工作区与有效 DPI。</summary>
internal static class DesktopMonitorResolver
{
    internal static DesktopMonitorTarget Resolve(Window? owner, Window? currentWindow = null)
    {
        var monitorHandle = ResolveMonitorHandle(owner, currentWindow);
        var monitorInfo = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitorHandle == IntPtr.Zero || !NativeWindowMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 DesktopAgent 目标显示器信息。");
        }

        var dpiX = 96u;
        var dpiY = 96u;
        if (NativeWindowMethods.GetDpiForMonitor(monitorHandle, 0, out var monitorDpiX, out var monitorDpiY) == 0
            && monitorDpiX > 0
            && monitorDpiY > 0)
        {
            dpiX = monitorDpiX;
            dpiY = monitorDpiY;
        }

        return new DesktopMonitorTarget(monitorHandle, monitorInfo.WorkArea, dpiX, dpiY);
    }

    private static IntPtr ResolveMonitorHandle(Window? owner, Window? currentWindow)
    {
        var candidate = owner ?? currentWindow;
        if (candidate is not null)
        {
            var handle = new WindowInteropHelper(candidate).Handle;
            if (handle != IntPtr.Zero)
            {
                return NativeWindowMethods.MonitorFromWindow(
                    handle,
                    NativeWindowMethods.MonitorDefaultToNearest);
            }
        }

        var foreground = NativeWindowMethods.GetForegroundWindow();
        var monitorHandle = foreground == IntPtr.Zero
            ? IntPtr.Zero
            : NativeWindowMethods.MonitorFromWindow(
                foreground,
                NativeWindowMethods.MonitorDefaultToNearest);
        return monitorHandle != IntPtr.Zero
            ? monitorHandle
            : NativeWindowMethods.MonitorFromPoint(
                new NativePoint(0, 0),
                NativeWindowMethods.MonitorDefaultToPrimary);
    }
}
