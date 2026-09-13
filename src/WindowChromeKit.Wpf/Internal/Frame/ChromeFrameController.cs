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
    private const uint WmSizing = 0x0214;
    private const int WmszLeft = 1;
    private const int WmszRight = 2;
    private const int WmszTop = 3;
    private const int WmszTopLeft = 4;
    private const int WmszTopRight = 5;
    private const int WmszBottom = 6;
    private const int WmszBottomLeft = 7;
    private const int WmszBottomRight = 8;

    /// <summary>最小尺寸里留给内容区的高度（DIP）。标准窗口的最小值去掉原生标题栏后也就剩这么一条。</summary>
    private const double MinimumContentHeightDip = 8d;
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
            case WmSizing:
                HandleSizing(wordParameter.ToInt32(), longParameter);
                handled = true;
                return IntPtr.Zero;
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
    /// 命中优先级：窗口矩形之外 → HTNOWHERE；客户区之外的那圈 frame → 缩放值（WPF 内容不可能在那里）；
    /// 客户区之内先看可交互元素（标题栏按钮、系统菜单图标、标记为 Client 的自定义标题栏内容），
    /// 它们占满整个标题栏高度；剩下只有标题栏空白处会让出顶部 6px 缩放带。
    /// </summary>
    private int HitTest(NativePoint pointer)
    {
        if (_handle == IntPtr.Zero || !NativeWindowMethods.GetWindowRect(_handle, out var windowRect))
            return WindowFrameHitTest.Client;
        // Chrome 实测：窗口矩形之外的任何点都返回 HTNOWHERE，绝不声明别人的像素
        if (!WindowFrameHitTest.Contains(windowRect, pointer))
            return WindowFrameHitTest.Nowhere;

        var dpi = ResolveDpi();
        var (frameX, frameY) = WindowFrameHitTest.GetFrameThickness(dpi);
        var maximized = NativeWindowMethods.IsZoomed(_handle);
        var client = WindowFrameHitTest.InsetToClient(windowRect, frameX, frameY, maximized);
        var resizable = _host.IsResizable;

        // 1) 客户区之外、窗口矩形之内：就是"阴影里那圈"不可见边框，只有缩放语义
        if (!WindowFrameHitTest.Contains(client, pointer))
        {
            if (!resizable)
                return WindowFrameHitTest.Client;
            var frameHit = WindowFrameHitTest.EvaluateResizeHit(
                pointer,
                windowRect,
                frameX,
                frameY,
                WindowFrameHitTest.GetTopResizeBand(dpi)
            );
            return frameHit;
        }

        // 2) 客户区之内：可交互元素优先，缩放带不能盖在它们上面
        var part = _input.ResolveChromePart(pointer, out var interactive);
        if (interactive || !resizable)
            return part;

        // 3) 客户区之内只有顶部带可能落在内容上（左右下三边都在客户区之外）；
        //    最大化时客户区正好等于工作区，纵向不可缩放，顶部整块交给标题栏。
        if (!maximized && pointer.Y < windowRect.Top + WindowFrameHitTest.GetTopResizeBand(dpi))
            return WindowFrameHitTest.Top;
        return part;
    }

    /// <summary>
    /// 窗口最小尺寸 = 内容之外还要放得下自绘标题栏与 frame。
    /// 系统默认的 SM_CXMINTRACK/SM_CYMINTRACK 是按"标准窗口（原生标题栏 23px）"给的，
    /// 我们的标题栏更高、客户区又内缩了 frame，直接沿用会把标题栏压扁；
    /// 因此最小高度 = 下 frame + 标题栏高度 + 一条内容，最小宽度 = 三个标题栏按钮 + 两侧 frame。
    /// </summary>
    private (int Width, int Height) GetMinimumTrackSize(uint dpi)
    {
        var (frameX, frameY) = WindowFrameHitTest.GetFrameThickness(dpi);
        var systemMinimumX = NativeWindowMethods.GetSystemMetricsForDpi(
            NativeWindowMethods.SmCxMinTrack,
            dpi
        );
        var systemMinimumY = NativeWindowMethods.GetSystemMetricsForDpi(
            NativeWindowMethods.SmCyMinTrack,
            dpi
        );
        var scale = (dpi == 0 ? 96u : dpi) / 96d;
        var caption = (int)Math.Ceiling(Math.Max(0, _host.CaptionHeight) * scale);
        // 图标区 + 三个标题栏按钮 + 两侧 frame：否则缩到最小时图标（系统菜单入口）
        // 和标题会被挤成 0，只剩三个按钮；标题本身可以截断，所以只保证图标与按钮。
        var leading = (int)Math.Ceiling(Math.Max(0, _host.CaptionLeadingWidth) * scale);
        var width = WindowFrameHitTest.CalculateMinimumTrackWidth(
            systemMinimumX,
            systemMinimumX,
            _host.IsResizable ? frameX * 2 : 0,
            _host.CaptionButtonsWidth + leading,
            dpi
        );
        var height = caption > 0
            ? Math.Max(
                systemMinimumY,
                frameY + caption + (int)Math.Ceiling(MinimumContentHeightDip * scale)
            )
            : systemMinimumY;
        return (width, height);
    }

    /// <summary>
    /// 拖动缩放时把矩形夹到最小尺寸：WPF 会用"标准窗口"的换算覆盖 WM_GETMINMAXINFO，
    /// 这里再兜一次底，保证标题栏不会被压扁。被拖动的那条边保持不变。
    /// </summary>
    private void HandleSizing(int edge, IntPtr longParameter)
    {
        if (longParameter == IntPtr.Zero)
            return;
        var rect = Marshal.PtrToStructure<NativeRectangle>(longParameter);
        var (minimumWidth, minimumHeight) = GetMinimumTrackSize(ResolveDpi());
        var width = Math.Max(rect.Width, minimumWidth);
        var height = Math.Max(rect.Height, minimumHeight);
        if (width == rect.Width && height == rect.Height)
            return;
        var draggingLeft = edge is WmszLeft or WmszTopLeft or WmszBottomLeft;
        var draggingTop = edge is WmszTop or WmszTopLeft or WmszTopRight;
        var updated = new NativeRectangle(
            draggingLeft ? rect.Right - width : rect.Left,
            draggingTop ? rect.Bottom - height : rect.Top,
            draggingLeft ? rect.Right : rect.Left + width,
            draggingTop ? rect.Bottom : rect.Top + height
        );
        Marshal.StructureToPtr(updated, longParameter, false);
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
        // 客户区左右各内缩一个 frame、顶部多出自绘标题栏，最小尺寸都要算进去，
        // 否则缩到最小时客户区放不下三个按钮、标题栏也会被压扁。
        var (minimumWidth, minimumHeight) = GetMinimumTrackSize(dpi);
        limits.MinTrackSize = new NativePoint(
            Math.Max(limits.MinTrackSize.X, minimumWidth),
            Math.Max(limits.MinTrackSize.Y, minimumHeight)
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
