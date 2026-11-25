using System;

namespace WindowChromeKit.WinForms.Internal;

/// <summary>
/// 测量 Win32 原生窗口**可见标题栏**的高度（DIP），作为标题栏图标的比例基准。
///
/// <para>与 WPF 侧的 <c>NativeCaptionMetrics</c> 同源同规则：<c>SM_CYCAPTION</c> 只算标题栏文字区，
/// 而用户看到的是"外框顶到客户区顶"的那段距离 —— 96dpi 实测 <c>SM_CYCAPTION</c> 是 23，
/// 实际却是 31。按 23 算会把图标放大过头。</para>
///
/// <para>只测一次并缓存。测量失败时退回 96dpi 实测值。</para>
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
            handle = NativeMethods.CreateWindowEx(
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

            if (!NativeMethods.GetWindowRect(handle, out var windowRect))
                return FallbackNativeCaptionHeight;

            var origin = default(NativePoint);
            if (!NativeMethods.ClientToScreen(handle, ref origin))
                return FallbackNativeCaptionHeight;

            var physical = origin.Y - windowRect.Top;
            if (physical <= 0)
                return FallbackNativeCaptionHeight;

            var dpi = NativeMethods.GetDpiForWindow(handle);
            if (dpi == 0)
                dpi = 96;

            var dip = physical * 96d / dpi;

            // 合理性校验：正常在 20~60 DIP。超出说明测量环境异常，用兜底值，
            // 避免算出荒谬的图标尺寸。
            return dip is >= 20d and <= 60d ? dip : FallbackNativeCaptionHeight;
        }
        catch
        {
            return FallbackNativeCaptionHeight;
        }
        finally
        {
            if (handle != IntPtr.Zero)
                _ = NativeMethods.DestroyWindow(handle);
        }
    }
}
