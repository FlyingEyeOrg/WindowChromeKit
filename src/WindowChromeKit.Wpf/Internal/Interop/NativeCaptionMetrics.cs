using System;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

/// <summary>
/// 测量 Win32 原生窗口**可见标题栏**的高度（DIP），作为标题栏图标的比例基准。
///
/// <para>做法：造一个最小的原生窗口（<c>WS_OVERLAPPEDWINDOW</c>，不显示），量出
/// 外框顶到客户区顶的距离，再按该窗口的 DPI 折算成 DIP。这个距离包含了标题栏、
/// 边框与顶部 resize 带 —— 正是用户视觉上看到的"标题栏"。</para>
///
/// <para>只测一次并缓存：系统度量在同一台机器、同一 DPI 下是稳定的，而这项测量
/// 需要创建窗口，不适合反复执行。</para>
/// </summary>
internal static class NativeCaptionMetrics
{
    /// <summary>96dpi 下的实测值，仅用于测量失败时兜底。</summary>
    internal const double FallbackNativeCaptionHeight = 31d;

    private static readonly object Gate = new();
    private static bool _measured;
    private static double _height = FallbackNativeCaptionHeight;

    /// <summary>原生可见标题栏高度（DIP）。首次访问时测量，失败则用 96dpi 实测值。</summary>
    internal static double Height
    {
        get
        {
            lock (Gate)
            {
                if (!_measured)
                {
                    _height = Measure();
                    _measured = true;
                }
                return _height;
            }
        }
    }

    private static double Measure()
    {
        var handle = IntPtr.Zero;
        try
        {
            handle = NativeWindowMethods.CreateWindowExW(
                0,
                "STATIC",
                string.Empty,
                // WS_OVERLAPPEDWINDOW：与普通顶层窗口一致的边框与标题栏
                0x00CF0000,
                0,
                0,
                200,
                100,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero
            );

            if (handle == IntPtr.Zero)
                return FallbackNativeCaptionHeight;

            if (!NativeWindowMethods.GetWindowRect(handle, out var windowRect))
                return FallbackNativeCaptionHeight;

            if (!NativeWindowMethods.GetClientRect(handle, out _))
                return FallbackNativeCaptionHeight;

            var origin = default(NativePoint);
            if (!NativeWindowMethods.ClientToScreen(handle, ref origin))
                return FallbackNativeCaptionHeight;

            var physical = origin.Y - windowRect.Top;
            if (physical <= 0)
                return FallbackNativeCaptionHeight;

            var dpi = NativeWindowMethods.GetDpiForWindow(handle);
            if (dpi == 0)
                dpi = 96;

            var dip = physical * 96d / dpi;

            // 合理性校验：正常在 20~60 DIP。超出说明测量环境异常（例如窗口没建成），
            // 用兜底值，避免算出荒谬的图标尺寸。
            return dip is >= 20d and <= 60d ? dip : FallbackNativeCaptionHeight;
        }
        catch
        {
            return FallbackNativeCaptionHeight;
        }
        finally
        {
            if (handle != IntPtr.Zero)
                _ = NativeWindowMethods.DestroyWindow(handle);
        }
    }
}
