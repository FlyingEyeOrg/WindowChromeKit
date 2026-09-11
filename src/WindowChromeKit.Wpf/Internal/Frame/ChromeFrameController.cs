using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowChromeKit.Wpf;

namespace WindowChromeKit.Wpf.Internal;

/// <summary>负责单个 ChromeWindow 的原生 frame 机制：消息钩子、DWM frame、resize overlay、DPI 与最大化工作区。</summary>
internal sealed class ChromeFrameController
{
    private const uint WmCancelMode = 0x001F;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmShowWindow = 0x0018;
    private const uint WmSettingChange = 0x001A;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmWindowPosChanged = 0x0047;
    private const uint WmDisplayChange = 0x007E;
    private const uint WmNcCalcSize = 0x0083;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmNcMouseMove = 0x00A0;
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint WmNcLButtonUp = 0x00A2;
    private const uint WmCaptureChanged = 0x0215;
    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const uint WmNcMouseLeave = 0x02A2;
    private const uint WmDwmCompositionChanged = 0x031E;
    private const uint WmDpiChanged = 0x02E0;
    private const int SpiSetWorkArea = 0x002F;
    private readonly IChromeFrameHost _host;
    private readonly ChromeInputController _input;
    private HwndSource? _source;
    private WindowResizeOverlay? _resizeOverlay;
    private IntPtr _handle;
    private bool _inSizeMove;
    private bool _nativeFrameRefreshPending;
    private bool _refreshingNativeFrame;
    private bool _disposed;

    internal ChromeFrameController(IChromeFrameHost host, ChromeInputController input)
    {
        _host = host;
        _input = input;
    }

    internal IntPtr Handle => _handle;

    internal IntPtr ResizeOverlayHandle => _resizeOverlay?.Handle ?? IntPtr.Zero;

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

    /// <summary>卸载窗口过程钩子，并释放 resize overlay。</summary>
    internal void Detach()
    {
        if (_disposed)
            return;
        _disposed = true;
        _input.CancelCaptionButtonPress();
        _resizeOverlay?.Dispose();
        _resizeOverlay = null;
        _source?.RemoveHook(WindowProcedure);
        _source = null;
    }

    internal void SynchronizeResizeOverlay() => _resizeOverlay?.Synchronize();

    /// <summary>按窗口是否可缩放创建或销毁外置 resize overlay。</summary>
    internal void UpdateResizeMode(bool isResizable)
    {
        if (_handle == IntPtr.Zero)
            return;
        if (isResizable)
        {
            // Overlay 与 owner 均由当前 Dispatcher 线程创建，HTTRANSPARENT 才能继续命中主窗口。
            _resizeOverlay ??= new WindowResizeOverlay(_handle, _input.GetRoleAtPoint);
            _resizeOverlay.Synchronize();
        }
        else
        {
            _resizeOverlay?.Dispose();
            _resizeOverlay = null;
        }
    }

    /// <summary>异步安排一次 DWM frame 刷新，避免在窗口消息中重复调用。</summary>
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

    /// <summary>重新应用 DWM frame 边距与 FRAMECHANGED，并同步 resize overlay。</summary>
    internal void RefreshNativeFrame()
    {
        if (_disposed || _handle == IntPtr.Zero || _refreshingNativeFrame)
            return;
        _refreshingNativeFrame = true;
        try
        {
            // 普通窗口向内扩展 1 像素：保留 DWM 阴影，又不会像 -1 那样把系统
            // 默认标题栏/按钮铺满整个客户区。最大化时窗口紧贴工作区，这 1 像素
            // DWM frame 会在屏幕边缘显示成白边；此时不需要可见阴影，改用 0 边距。
            var margins = NativeWindowMethods.IsZoomed(_handle)
                ? new NativeMargins(0, 0, 0, 0)
                : new NativeMargins(1, 1, 1, 1);
            _ = NativeWindowMethods.DwmExtendFrameIntoClientArea(_handle, ref margins);
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
            _host.RefreshLayout();
            _resizeOverlay?.Synchronize();
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
            case WmShowWindow:
                if (wordParameter == IntPtr.Zero)
                    _resizeOverlay?.Hide();
                else if (!_inSizeMove)
                    _host.Dispatcher.BeginInvoke(() => _resizeOverlay?.Synchronize());
                break;
            case WmNcHitTest:
                handled = true;
                return new IntPtr(_input.HitTest(GetScreenPoint(longParameter)));
            case WmWindowPosChanged:
                if (!_inSizeMove)
                    _resizeOverlay?.Synchronize();
                break;
            case WmDwmCompositionChanged:
                ScheduleNativeFrameRefresh();
                break;
            case WmEnterSizeMove:
                _inSizeMove = true;
                _resizeOverlay?.Hide();
                break;
            case WmExitSizeMove:
                _inSizeMove = false;
                _resizeOverlay?.Synchronize();
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

    /// <summary>设置最大化尺寸/位置，并按标题栏按钮宽度计算最小跟踪宽度。</summary>
    private void HandleGetMinMaxInfo(IntPtr window, IntPtr longParameter)
    {
        if (longParameter == IntPtr.Zero)
            return;
        var limits = Marshal.PtrToStructure<NativeMinMaxInfo>(longParameter);
        var dpi = NativeWindowMethods.GetDpiForWindow(window);
        if (dpi == 0)
            dpi = 96;
        var resizeBorderWidth =
            NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxFrame, dpi)
            + NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxPaddedBorder, dpi);
        var systemMinimum = NativeWindowMethods.GetSystemMetricsForDpi(
            NativeWindowMethods.SmCxMinTrack,
            dpi
        );
        limits.MinTrackSize = new NativePoint(
            WindowFrameHitTest.CalculateMinimumTrackWidth(
                limits.MinTrackSize.X,
                systemMinimum,
                _host.IsResizable ? resizeBorderWidth : 0,
                _host.CaptionButtonsWidth,
                dpi
            ),
            limits.MinTrackSize.Y
        );
        var monitor = NativeWindowMethods.MonitorFromWindow(
            window,
            NativeWindowMethods.MonitorDefaultToNearest
        );
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitor != IntPtr.Zero && NativeWindowMethods.GetMonitorInfo(monitor, ref info))
        {
            var placement = WindowFrameHitTest.CalculateMaximizedPlacement(
                info.Monitor,
                info.WorkArea
            );
            limits.MaxPosition = placement.Position;
            limits.MaxSize = placement.Size;
        }
        Marshal.StructureToPtr(limits, longParameter, false);
    }

    /// <summary>最大化或接近最大化时，把客户区限制在显示器工作区内。</summary>
    private static void HandleNcCalcSize(IntPtr window, IntPtr wordParameter, IntPtr longParameter)
    {
        if (longParameter == IntPtr.Zero)
            return;
        var monitor = NativeWindowMethods.MonitorFromWindow(
            window,
            NativeWindowMethods.MonitorDefaultToNearest
        );
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeWindowMethods.GetMonitorInfo(monitor, ref info))
            return;
        var proposed =
            wordParameter != IntPtr.Zero
                ? Marshal.PtrToStructure<NativeNcCalcSizeParameters>(longParameter).Proposed
                : Marshal.PtrToStructure<NativeRectangle>(longParameter);
        var dpi = NativeWindowMethods.GetDpiForWindow(window);
        if (dpi == 0)
            dpi = 96;
        var borderX =
            NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxFrame, dpi)
            + NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxPaddedBorder, dpi);
        var borderY =
            NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCyFrame, dpi)
            + NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxPaddedBorder, dpi);
        if (
            !NativeWindowMethods.IsZoomed(window)
            && !WindowFrameHitTest.MatchesMaximizedBounds(proposed, info.WorkArea, borderX, borderY)
        )
            return;
        var client = WindowFrameHitTest.ClampToWorkArea(proposed, info.WorkArea);
        if (wordParameter != IntPtr.Zero)
        {
            var parameters = Marshal.PtrToStructure<NativeNcCalcSizeParameters>(longParameter);
            parameters.Proposed = client;
            Marshal.StructureToPtr(parameters, longParameter, false);
        }
        else
        {
            Marshal.StructureToPtr(client, longParameter, false);
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
