using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

/// <summary>提供窗口居中和工作区约束的静态服务。</summary>
public static class WindowPlacementService
{
    /// <summary>在所有者所在的显示器居中；没有所有者时在前台窗口所在显示器居中。</summary>
    public static void CenterOnTargetMonitor(Window window)
    {
        if (window is null)
            throw new ArgumentNullException(nameof(window));
        var handle = GetHandle(window);
        if (handle == IntPtr.Zero)
            return;
        var target = MonitorResolver.Resolve(window.Owner, window);
        NativeRectangle? ownerBounds = null;
        if (window.Owner is not null)
        {
            var ownerHandle = new WindowInteropHelper(window.Owner).Handle;
            if (
                ownerHandle != IntPtr.Zero
                && NativeWindowMethods.GetWindowRect(ownerHandle, out var rectangle)
            )
                ownerBounds = rectangle;
        }
        SetNativeBounds(
            handle,
            WindowPlacement.Center(
                target,
                EffectiveWidth(window),
                EffectiveHeight(window),
                ownerBounds
            )
        );
    }

    /// <summary>移动并调整窗口，使窗口边界位于最近显示器的工作区内。</summary>
    public static void ConstrainToWorkArea(Window window)
    {
        if (window is null)
            throw new ArgumentNullException(nameof(window));
        var handle = GetHandle(window);
        if (handle == IntPtr.Zero)
            return;
        if (!NativeWindowMethods.GetWindowRect(handle, out var current))
            return;
        SetNativeBounds(
            handle,
            WindowPlacement.Clamp(MonitorResolver.Resolve(null, window), current)
        );
    }

    internal static void SetNativeBounds(IntPtr handle, NativeRectangle bounds)
    {
        if (
            !NativeWindowMethods.SetWindowPos(
                handle,
                IntPtr.Zero,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                NativeWindowMethods.SwpNoZOrder
                    | NativeWindowMethods.SwpNoActivate
                    | NativeWindowMethods.SwpNoOwnerZOrder
            )
        )
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置窗口位置。");
        }
    }

    private static IntPtr GetHandle(Window window) => new WindowInteropHelper(window).Handle;

    private static double EffectiveWidth(Window window) =>
        IsFinitePositive(window.Width) ? window.Width
        : IsFinitePositive(window.ActualWidth) ? window.ActualWidth
        : Math.Max(1, window.MinWidth);

    private static double EffectiveHeight(Window window) =>
        IsFinitePositive(window.Height) ? window.Height
        : IsFinitePositive(window.ActualHeight) ? window.ActualHeight
        : Math.Max(1, window.MinHeight);

    private static bool IsFinitePositive(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
