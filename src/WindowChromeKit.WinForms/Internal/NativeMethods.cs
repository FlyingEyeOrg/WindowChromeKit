using System.Runtime.InteropServices;

namespace WindowChromeKit.WinForms.Internal;

/// <summary>本程序集需要的 Win32 互操作声明。数值与 C++ 示例 / 真实 Chrome 实测保持一致。</summary>
internal static class NativeMethods
{
    internal const int WmNcCalcSize = 0x0083;
    internal const int WmNcHitTest = 0x0084;
    internal const int WmNcMouseMove = 0x00A0;
    internal const int WmNcLButtonDown = 0x00A1;
    internal const int WmNcLButtonDblClk = 0x00A3;
    internal const int WmNcMouseLeave = 0x02A2;
    internal const int WmMouseMove = 0x0200;
    internal const int WmLButtonUp = 0x0202;
    internal const int WmCaptureChanged = 0x0215;
    internal const int WmDpiChanged = 0x02E0;
    internal const int WmGetMinMaxInfo = 0x0024;
    internal const int WmSizing = 0x0214;

    internal const int WmszLeft = 1;
    internal const int WmszRight = 2;
    internal const int WmszTop = 3;
    internal const int WmszTopLeft = 4;
    internal const int WmszTopRight = 5;
    internal const int WmszBottom = 6;
    internal const int WmszBottomLeft = 7;
    internal const int WmszBottomRight = 8;
    internal const int WmSize = 0x0005;
    internal const int WmActivate = 0x0006;
    internal const int WmSettingChange = 0x001A;
    internal const int WmThemeChanged = 0x031A;
    internal const int WmDwmCompositionChanged = 0x031E;

    internal const int SmCxFrame = 32;
    internal const int SmCyFrame = 33;
    internal const int SmCxPaddedBorder = 92;
    internal const int SmCxMinTrack = 34;
    internal const int SmCxSmSize = 52;   // 标题栏"小按钮"尺寸 = 系统菜单图标盒边长
    internal const int SmCySmSize = 53;
    internal const int SmCyMinTrack = 35;
    internal const int SmCxSmIcon = 49;
    internal const int SmCySmIcon = 50;

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;

    internal const int IconSmall = 0;
    internal const int IconSmall2 = 2;
    internal const int IconBig = 1;
    internal const int IdiApplication = 32512;

    internal const int DwmwaNcRenderingEnabled = 1;
    internal const int DwmwaNcRenderingPolicy = 2;

    // DWMNCRENDERINGPOLICY：USEWINDOWSTYLE = 0, DISABLED = 1, ENABLED = 2
    internal const int DwmNcrpEnabled = 2;

    [DllImport("user32.dll", EntryPoint = "GetSystemMetricsForDpi")]
    private static extern int GetSystemMetricsForDpiCore(int index, uint dpi);

    [DllImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    private static extern int GetSystemMetricsCore(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetCapture(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetSystemMenu(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool revert);

    [DllImport("user32.dll")]
    internal static extern int TrackPopupMenuEx(
        IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);

    internal const int WmSysCommand = 0x0112;
    internal const uint TpmLeftAlign = 0x0000;
    internal const uint TpmLeftButton = 0x0000;
    internal const uint TpmReturnCmd = 0x0100;
    internal const uint TpmNonotify = 0x0080;

    [DllImport("user32.dll")]
    internal static extern bool TrackMouseEvent(ref NativeTrackMouseEvent track);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "DrawIconEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DrawIconEx(
        IntPtr deviceContext,
        int x,
        int y,
        IntPtr icon,
        int width,
        int height,
        uint animationStep,
        IntPtr flickerFreeBrush,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    internal static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("dwmapi.dll", EntryPoint = "DwmExtendFrameIntoClientArea")]
    private static extern int DwmExtendFrameIntoClientAreaCore(IntPtr window, ref NativeMargins margins);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttributeCore(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeCore(IntPtr window, int attribute, out int value, int size);

    /// <summary>按 DPI 取系统度量；老系统上没有 <c>GetSystemMetricsForDpi</c> 时按比例缩放回退。</summary>
    internal static int GetSystemMetricsForDpi(int index, uint dpi)
    {
        var effectiveDpi = dpi == 0 ? 96u : dpi;
        try
        {
            return GetSystemMetricsForDpiCore(index, effectiveDpi);
        }
        catch (EntryPointNotFoundException)
        {
            return (int)((long)GetSystemMetricsCore(index) * effectiveDpi / 96);
        }
    }

    /// <summary>
    /// 取窗口小图标，回退顺序与 WPF 版的 <c>RefreshEffectiveTitleBarIcon</c> **完全一致**：
    /// <c>WM_GETICON(SMALL2) → SMALL → BIG → IDI_APPLICATION</c>。
    ///
    /// 为什么要和 WPF 一致：两库同名同语义的窗口基类，标题栏图标的来源不该有差异。
    /// 这里刻意**不问窗口类图标、也不读 exe 图标** —— WinForms 会把
    /// <c>&lt;ApplicationIcon&gt;</c> 直接设成窗口图标，所以 <c>WM_GETICON</c> 第一步就命中，
    /// 那两步实测恒为空（见下方注释）；只有 <c>Icon</c> 完全没设且宿主也用
    /// <c>SetClassLongPtr</c> 设了类图标这种边界场景，才轮到 <c>BIG</c> 兜底。
    /// </summary>
    internal static IntPtr GetWindowSmallIcon(IntPtr window)
    {
        // SMALL2 是任务栏用的那张；实测在"未设 Icon""Icon = null""显式设 Icon"三种情况下
        // 都返回真图标，因此后续步骤基本不会执行 —— 顺序的意义在于可预测与跨库一致。
        var icon = SendMessage(window, 0x007F /* WM_GETICON */, new IntPtr(IconSmall2), IntPtr.Zero);
        if (icon != IntPtr.Zero)
            return icon;
        icon = SendMessage(window, 0x007F, new IntPtr(IconSmall), IntPtr.Zero);
        if (icon != IntPtr.Zero)
            return icon;
        icon = SendMessage(window, 0x007F, new IntPtr(IconBig), IntPtr.Zero);
        if (icon != IntPtr.Zero)
            return icon;
        return LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication));
    }

    /// <summary>把 DWM frame 向内扩展，强制 DWM 为该窗口渲染 frame（阴影、边框线来自它）。
    /// WinForms 的窗口默认拿不到 frame 渲染（实测 NCRENDERING_ENABLED=0），必须显式扩展。</summary>
    internal static void ExtendFrameIntoClientArea(IntPtr window, int thickness)
    {
        var margins = new NativeMargins(thickness, thickness, thickness, thickness);
        _ = DwmExtendFrameIntoClientAreaCore(window, ref margins);
    }

    /// <summary>保持 DWM 渲染非客户区（阴影、可见边框、不可见缩放带都来自它）。</summary>
    internal static int EnsureNonClientRendering(IntPtr window)
    {
        var policy = DwmNcrpEnabled;
        return DwmSetWindowAttributeCore(window, DwmwaNcRenderingPolicy, ref policy, sizeof(int));
    }

    /// <summary>查询 DWM 是否为该窗口渲染非客户区（诊断与验证用）。</summary>
    internal static bool IsNonClientRenderingEnabled(IntPtr window, out int value)
    {
        value = 0;
        return DwmGetWindowAttributeCore(window, DwmwaNcRenderingEnabled, out value, sizeof(int)) == 0;
    }
}
