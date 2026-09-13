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
    internal const int SmCyMinTrack = 35;
    internal const int SmCxSmIcon = 49;
    internal const int SmCySmIcon = 50;

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;

    internal const int GclpHiconSm = -34;
    internal const int GclpHicon = -14;

    internal const int IconSmall = 0;
    internal const int IconSmall2 = 2;

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

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
    private static extern int GetClassLong32(IntPtr window, int index);

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

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string? fileName, int iconIndex, out IntPtr large, out IntPtr small, uint count);

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("dwmapi.dll", EntryPoint = "DwmExtendFrameIntoClientArea")]
    private static extern int DwmExtendFrameIntoClientAreaCore(IntPtr window, ref NativeMargins margins);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttributeCore(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeCore(IntPtr window, int attribute, out int value, int size);

    private static readonly IntPtr IdiApplication = new(32512);
    private static IntPtr _applicationIcon;
    private static bool _applicationIconResolved;

    /// <summary>取可执行文件自己的小图标（壳层 API，只在首次调用时解析并缓存）。</summary>
    private static IntPtr GetApplicationIcon()
    {
        if (_applicationIconResolved)
            return _applicationIcon;
        _applicationIconResolved = true;
        try
        {
            var path = System.Windows.Forms.Application.ExecutablePath;
            if (string.IsNullOrEmpty(path))
                return IntPtr.Zero;
            // 返回值为图标数量，0 表示文件里没有图标
            if (ExtractIconEx(path, 0, out var large, out var small, 1) == 0)
                return IntPtr.Zero;
            if (large != IntPtr.Zero)
                _ = DestroyIcon(large);
            _applicationIcon = small;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            _applicationIcon = IntPtr.Zero;
        }
        return _applicationIcon;
    }

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

    /// <summary>取窗口类的小图标（窗口没有显式设置图标时的回退）。</summary>
    internal static IntPtr GetClassSmallIcon(IntPtr window)
    {
        var icon = GetClassIcon(window, GclpHiconSm);
        return icon != IntPtr.Zero ? icon : GetClassIcon(window, GclpHicon);
    }

    private static IntPtr GetClassIcon(IntPtr window, int index) =>
        IntPtr.Size == 8
            ? GetClassLongPtr64(window, index)
            : new IntPtr(GetClassLong32(window, index));

    /// <summary>与原生标题栏一致地取窗口小图标：先问窗口，再问窗口类。</summary>
    internal static IntPtr GetWindowSmallIcon(IntPtr window)
    {
        var icon = SendMessage(window, 0x007F /* WM_GETICON */, new IntPtr(IconSmall2), IntPtr.Zero);
        if (icon != IntPtr.Zero)
            return icon;
        icon = SendMessage(window, 0x007F, new IntPtr(IconSmall), IntPtr.Zero);
        if (icon != IntPtr.Zero)
            return icon;
        icon = GetClassSmallIcon(window);
        if (icon != IntPtr.Zero)
            return icon;
        // 最后回退到 exe 自己的图标（<ApplicationIcon> 嵌进去的那个），
        // 相当于 C++ 示例里 app.rc 提供的窗口图标；再不行才用系统默认图标。
        icon = GetApplicationIcon();
        return icon != IntPtr.Zero ? icon : LoadIcon(IntPtr.Zero, IdiApplication);
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
