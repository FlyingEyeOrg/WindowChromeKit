using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowChromeKit.Wpf;

namespace WindowChromeKit.Wpf.Internal;

/// <summary>
/// 负责单个 ChromeWindow 的原生 frame 机制：消息钩子、客户区内缩、命中判定、DPI 与最大化度量。
///
/// 与 C++ 示例（<c>WindowChromeKit.Native.Sample</c>）同一套模型：保留
/// <c>WS_CAPTION | WS_THICKFRAME</c>，由 DWM 提供阴影与可见边框；
/// <c>WM_NCCALCSIZE</c> 把客户区从窗口矩形内缩出 frame 区域（普通态顶部不内缩），
/// <c>WM_NCHITTEST</c> 按 Chrome 的优先级自行判定，不再需要外置 overlay 子窗口。
/// </summary>
internal sealed class ChromeFrameController
{
    private const uint WmSize = 0x0005;
    private const uint WmCancelMode = 0x001F;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmSettingChange = 0x001A;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmDisplayChange = 0x007E;
    private const uint WmNcCalcSize = 0x0083;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmNcMouseMove = 0x00A0;
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint WmNcLButtonUp = 0x00A2;
    private const uint WmCaptureChanged = 0x0215;
    private const uint WmNcMouseLeave = 0x02A2;
    private const uint WmDpiChanged = 0x02E0;
    private const uint WmDwmCompositionChanged = 0x031E;
    private const int SpiSetWorkArea = 0x002F;
    private readonly IChromeFrameHost _host;
    private readonly ChromeInputController _input;
    private HwndSource? _source;
    private IntPtr _handle;
    private bool _nativeFrameRefreshPending;
    private bool _refreshingNativeFrame;
    private bool _disposed;

    internal ChromeFrameController(IChromeFrameHost host, ChromeInputController input)
    {
        _host = host;
        _input = input;
    }

    internal IntPtr Handle => _handle;

    /// <summary>挂载窗口过程钩子，并初始化未渲染区域的合成背景。</summary>
    internal void Attach(HwndSource? source, IntPtr handle)
    {
        _source = source;
        _handle = handle;
        // 未渲染区域用窗口背景色兜底，避免 resize 时露出默认帧或桌面。
        if (_source?.CompositionTarget is { } target)
            target.BackgroundColor = _host.CompositionBackgroundColor;
        _source?.AddHook(WindowProcedure);
    }

    /// <summary>卸载窗口过程钩子。</summary>
    internal void Detach()
    {
        if (_disposed)
            return;
        _disposed = true;
        _input.CancelCaptionButtonPress();
        _source?.RemoveHook(WindowProcedure);
        _source = null;
    }

    /// <summary>异步安排一次 frame 刷新，避免在窗口消息中重复调用。</summary>
    internal void ScheduleNativeFrameRefresh()
    {
        if (_disposed || _handle == IntPtr.Zero || _nativeFrameRefreshPending)
            return;
        _nativeFrameRefreshPending = true;
        _host.Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                _nativeFrameRefreshPending = false;
                RefreshNativeFrame();
            })
        );
    }

    /// <summary>
    /// 重新应用原生 frame 并让 WPF 重新布局。
    /// <paramref name="notifyClientSize"/> 为 true 时补发一次 WM_SIZE：
    /// 初始化阶段客户区刚被 WM_NCCALCSIZE 改过，而 SWP_FRAMECHANGED 本身不产生 WM_SIZE，
    /// WPF 会继续按旧客户区布局（内容比客户区宽/高）。最大化、还原这类状态切换本身
    /// 会带真实的 WM_SIZE，补发反而会用过期尺寸覆盖，因此只在初始化时补。
    /// </summary>
    internal void RefreshNativeFrame(bool notifyClientSize = false)
    {
        if (_disposed || _handle == IntPtr.Zero || _refreshingNativeFrame)
            return;
        _refreshingNativeFrame = true;
        try
        {
            // 客户区由 WM_NCCALCSIZE 内缩，DWM 因此会渲染窗口 frame（阴影、可见边框、
            // 不可见缩放带都来自它）；这里只需要让系统重算一次 frame。
            _ = NativeWindowMethods.SetWindowPos(
                _handle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                NativeWindowMethods.SwpFrameChanged
                    | NativeWindowMethods.SwpNoZOrder
                    | NativeWindowMethods.SwpNoActivate
                    | NativeWindowMethods.SwpNoOwnerZOrder
                    | NativeWindowMethods.SwpNoSize
                    | NativeWindowMethods.SwpNoMove
            );
            if (notifyClientSize && NativeWindowMethods.GetClientRect(_handle, out var client))
            {
                _ = NativeWindowMethods.SendMessage(
                    _handle,
                    WmSize,
                    IntPtr.Zero,
                    new IntPtr(unchecked((client.Height << 16) | (client.Width & 0xFFFF)))
                );
            }
            _host.RefreshLayout();
        }
        finally
        {
            _refreshingNativeFrame = false;
        }
    }

    /// <summary>原生窗口过程：只处理 frame 与输入路由，业务逻辑仍留在 ChromeWindow。</summary>
    private IntPtr WindowProcedure(
        IntPtr window,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled
    )
    {
        switch ((uint)message)
        {
            case WmGetMinMaxInfo:
                HandleGetMinMaxInfo(window, longParameter);
                break;
            case WmNcCalcSize:
                HandleNcCalcSize(window, wordParameter, longParameter);
                handled = true;
                return IntPtr.Zero;
            case WmNcHitTest:
                handled = true;
                return new IntPtr(HitTest(GetScreenPoint(longParameter)));
            case WmDwmCompositionChanged:
                ScheduleNativeFrameRefresh();
                break;
            case WmDpiChanged:
                _host.Dispatcher.BeginInvoke(() =>
                {
                    _host.OnFrameDpiChanged();
                    ScheduleNativeFrameRefresh();
                });
                break;
            case WmDisplayChange:
            case WmSettingChange:
                _host.Dispatcher.BeginInvoke(() =>
                {
                    _host.OnFrameDpiChanged();
                    ScheduleNativeFrameRefresh();
                    if (
                        (uint)message == WmDisplayChange
                        || wordParameter.ToInt64() == SpiSetWorkArea
                    )
                        _host.OnDisplayConfigurationChanged();
                });
                break;
            case WmNcMouseMove:
                _input.ObserveNonClientMove(wordParameter.ToInt32());
                break;
            case WmNcLButtonDown:
                if (_input.HandleNcLButtonDown(window, wordParameter.ToInt32()))
                {
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
            case WmNcLButtonUp:
                if (_input.HandleNcLButtonUp(wordParameter.ToInt32()))
                {
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
            case WmMouseMove:
                if (_input.HandleMouseMove())
                {
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
            case WmLButtonUp:
                if (_input.HandleLButtonUp())
                {
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
            case WmNcMouseLeave:
                if (!_input.IsTrackingCaptionButtonPress)
                    _input.ResetPointerVisualState();
                break;
            case WmCancelMode:
                _input.CancelCaptionButtonPress();
                break;
            case WmCaptureChanged:
                _input.HandleCaptureChanged();
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 命中优先级与 C++ 示例一致：窗口矩形之外 → HTNOWHERE；标题栏按钮 → 各自的值；
    /// 左/右/下与四角、顶部带 → 缩放值；图标区 → HTSYSMENU；标题栏 → HTCAPTION；其余 → HTCLIENT。
    /// </summary>
    private int HitTest(NativePoint pointer)
    {
        if (_handle == IntPtr.Zero || !NativeWindowMethods.GetWindowRect(_handle, out var windowRect))
            return WindowFrameHitTest.Client;
        // Chrome 实测：窗口矩形之外的任何点都返回 HTNOWHERE，绝不声明别人的像素
        if (!WindowFrameHitTest.Contains(windowRect, pointer))
            return WindowFrameHitTest.Nowhere;

        // 1) 标题栏按钮优先（角色由 WPF 视觉树给出：按钮、图标区、标题栏、客户区）
        var part = _input.ResolveNativePart(pointer);
        if (WindowFrameHitTest.IsCaptionButtonHit(part))
            return part;

        // 2) 左/右/下三边与四角；顶部带最窄，最大化时纵向不可缩放
        if (_host.IsResizable)
        {
            var dpi = ResolveDpi();
            var (frameX, frameY) = WindowFrameHitTest.GetFrameThickness(dpi);
            var hit = WindowFrameHitTest.EvaluateResizeHit(
                pointer,
                windowRect,
                frameX,
                frameY,
                WindowFrameHitTest.GetTopResizeBand(dpi)
            );
            if (hit == WindowFrameHitTest.Top && NativeWindowMethods.IsZoomed(_handle))
                hit = WindowFrameHitTest.Client;
            if (hit != WindowFrameHitTest.Client)
                return hit;
        }

        // 3) 系统菜单 / 标题栏 / 客户区
        return part;
    }

    private uint ResolveDpi()
    {
        var dpi = NativeWindowMethods.GetDpiForWindow(_handle);
        return dpi == 0 ? 96u : dpi;
    }

    /// <summary>按标题栏按钮宽度计算最小跟踪宽度；最大化位置交给系统默认值。</summary>
    private void HandleGetMinMaxInfo(IntPtr window, IntPtr longParameter)
    {
        if (longParameter == IntPtr.Zero)
            return;
        var limits = Marshal.PtrToStructure<NativeMinMaxInfo>(longParameter);
        var dpi = NativeWindowMethods.GetDpiForWindow(window);
        if (dpi == 0)
            dpi = 96;
        var (frameX, _) = WindowFrameHitTest.GetFrameThickness(dpi);
        var systemMinimum = NativeWindowMethods.GetSystemMetricsForDpi(
            NativeWindowMethods.SmCxMinTrack,
            dpi
        );
        // 客户区左右各内缩一个 frame，所以最小宽度要留出两侧 frame，
        // 否则缩到最窄时客户区放不下三个标题栏按钮。
        limits.MinTrackSize = new NativePoint(
            WindowFrameHitTest.CalculateMinimumTrackWidth(
                limits.MinTrackSize.X,
                systemMinimum,
                _host.IsResizable ? frameX * 2 : 0,
                _host.CaptionButtonsWidth,
                dpi
            ),
            limits.MinTrackSize.Y
        );
        Marshal.StructureToPtr(limits, longParameter, false);
    }

    /// <summary>
    /// 客户区 = 窗口矩形内缩出 frame 区域（普通态顶部不内缩）。
    /// 最大化时窗口矩形是工作区外扩一个 frame，内缩后客户区正好等于工作区。
    /// </summary>
    private static void HandleNcCalcSize(IntPtr window, IntPtr wordParameter, IntPtr longParameter)
    {
        if (longParameter == IntPtr.Zero)
            return;
        var dpi = NativeWindowMethods.GetDpiForWindow(window);
        if (dpi == 0)
            dpi = 96;
        var (frameX, frameY) = WindowFrameHitTest.GetFrameThickness(dpi);
        var maximized = NativeWindowMethods.IsZoomed(window);
        if (wordParameter != IntPtr.Zero)
        {
            var parameters = Marshal.PtrToStructure<NativeNcCalcSizeParameters>(longParameter);
            parameters.Proposed = WindowFrameHitTest.InsetToClient(
                parameters.Proposed,
                frameX,
                frameY,
                maximized
            );
            Marshal.StructureToPtr(parameters, longParameter, false);
        }
        else
        {
            var proposed = Marshal.PtrToStructure<NativeRectangle>(longParameter);
            Marshal.StructureToPtr(
                WindowFrameHitTest.InsetToClient(proposed, frameX, frameY, maximized),
                longParameter,
                false
            );
        }
    }

    private static NativePoint GetScreenPoint(IntPtr longParameter)
    {
        var packed = longParameter.ToInt64();
        return new NativePoint(
            unchecked((short)(packed & 0xffff)),
            unchecked((short)((packed >> 16) & 0xffff))
        );
    }
}
