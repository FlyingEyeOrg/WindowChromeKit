using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowChromeKit.Wpf.Internal;

/// <summary>在主窗口外侧提供原生 resize 命中的透明 owned HWND。</summary>
internal sealed class WindowResizeOverlay : IDisposable
{
    internal const string WindowClassName = "WindowChromeKit.Wpf.ResizeOverlay";

    private const uint WmEraseBackground = 0x0014;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint WmNcLButtonDoubleClick = 0x00A3;
    private const int HitNowhere = 0;
    internal const int HitTransparent = -1;
    private const int MouseActivateNoActivate = 3;
    private const int RegionDifference = 4;
    private const int ErrorClassAlreadyExists = 1410;
    private const uint WindowStylePopup = 0x80000000;
    private const uint WindowExStyleToolWindow = 0x00000080;
    private const uint WindowExStyleNoRedirectionBitmap = 0x00200000;
    private const uint WindowExStyleNoActivate = 0x08000000;

    private static readonly object ClassGate = new();
    private static readonly Dictionary<IntPtr, WindowResizeOverlay> Instances = [];
    private static readonly NativeWindowMethods.WindowProcedure SharedWindowProcedure = WindowProcedure;
    private static bool _classRegistered;

    private readonly IntPtr _owner;
    private readonly Func<NativePoint, bool> _isCaptionButton;
    private IntPtr _handle;
    private bool _disposed;

    internal WindowResizeOverlay(IntPtr owner, Func<NativePoint, bool> isCaptionButton)
    {
        if (owner == IntPtr.Zero) throw new ArgumentException("Owner HWND 不能为空。", nameof(owner));
        ArgumentNullException.ThrowIfNull(isCaptionButton);
        _owner = owner;
        _isCaptionButton = isCaptionButton;
        RegisterWindowClass();
        _handle = NativeWindowMethods.CreateWindowEx(
            WindowExStyleNoRedirectionBitmap | WindowExStyleNoActivate | WindowExStyleToolWindow,
            WindowClassName,
            string.Empty,
            WindowStylePopup,
            0,
            0,
            0,
            0,
            owner,
            IntPtr.Zero,
            NativeWindowMethods.GetModuleHandle(null),
            IntPtr.Zero);
        if (_handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建 WindowChromeKit resize overlay HWND。");
        Instances.Add(_handle, this);
    }

    internal IntPtr Handle => _handle;

    internal void Synchronize()
    {
        if (_disposed || _handle == IntPtr.Zero) return;
        if (!NativeWindowMethods.IsWindowVisible(_owner)
            || !NativeWindowMethods.IsWindowEnabled(_owner)
            || NativeWindowMethods.IsIconic(_owner)
            || NativeWindowMethods.IsZoomed(_owner))
        {
            Hide();
            return;
        }

        if (!NativeWindowMethods.GetWindowRect(_owner, out var bounds))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取主窗口矩形。");
        var (borderX, borderY) = GetResizeBorder();
        var overlay = CalculateBounds(bounds, borderX, borderY);
        if (!NativeWindowMethods.SetWindowPos(
                _handle,
                IntPtr.Zero,
                overlay.Left,
                overlay.Top,
                overlay.Width,
                overlay.Height,
                NativeWindowMethods.SwpNoZOrder
                | NativeWindowMethods.SwpNoActivate
                | NativeWindowMethods.SwpNoOwnerZOrder
                | NativeWindowMethods.SwpShowWindow))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法同步 resize overlay。");
        }

        ApplyRingRegion(overlay.Width, overlay.Height, borderX, borderY);
    }

    internal void Hide()
    {
        if (_handle != IntPtr.Zero)
            _ = NativeWindowMethods.ShowWindow(_handle, NativeWindowMethods.ShowWindowHide);
    }

    private void ApplyRingRegion(int width, int height, int borderX, int borderY)
    {
        var outer = NativeWindowMethods.CreateRectRegion(0, 0, width, height);
        if (outer == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建 resize overlay 外部区域。");

        IntPtr inner = IntPtr.Zero;
        try
        {
            if (width > borderX * 2 && height > borderY * 2)
            {
                inner = NativeWindowMethods.CreateRectRegion(borderX, borderY, width - borderX, height - borderY);
                if (inner == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建 resize overlay 内部区域。");
                _ = NativeWindowMethods.CombineRegion(outer, outer, inner, RegionDifference);
            }

            if (NativeWindowMethods.SetWindowRegion(_handle, outer, redraw: true) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置 DesktopAgent resize overlay 区域。");
            outer = IntPtr.Zero;
        }
        finally
        {
            if (inner != IntPtr.Zero) _ = NativeWindowMethods.DeleteObject(inner);
            if (outer != IntPtr.Zero) _ = NativeWindowMethods.DeleteObject(outer);
        }
    }

    private IntPtr HandleMessage(uint message, IntPtr wordParameter, IntPtr longParameter)
    {
        switch (message)
        {
            case WmNcHitTest:
                return new IntPtr(HitTest(longParameter));
            case WmNcLButtonDown:
            case WmNcLButtonDoubleClick:
                if (IsResizeHit(wordParameter.ToInt32()))
                {
                    Hide();
                    _ = NativeWindowMethods.SendMessage(_owner, message, wordParameter, longParameter);
                    Synchronize();
                    return IntPtr.Zero;
                }
                break;
            case WmMouseActivate:
                return new IntPtr(MouseActivateNoActivate);
            case WmEraseBackground:
                return new IntPtr(1);
        }

        return NativeWindowMethods.DefWindowProc(_handle, message, wordParameter, longParameter);
    }

    private int HitTest(IntPtr longParameter)
    {
        if (!NativeWindowMethods.IsWindowEnabled(_owner)) return HitNowhere;
        var packed = longParameter.ToInt64();
        var pointer = new NativePoint(
            unchecked((short)(packed & 0xffff)),
            unchecked((short)((packed >> 16) & 0xffff)));
        if (_isCaptionButton(pointer)) return HitTransparent;
        if (!NativeWindowMethods.GetWindowRect(_handle, out var bounds)) return WindowFrameHitTest.Client;
        var (borderX, borderY) = GetResizeBorder();
        return EvaluateHit(pointer, bounds, borderX, borderY);
    }

    internal static NativeRectangle CalculateBounds(NativeRectangle owner, int borderX, int borderY)
    {
        borderX = Math.Max(1, borderX);
        borderY = Math.Max(1, borderY);
        return new NativeRectangle(
            owner.Left - borderX,
            owner.Top,
            owner.Right + borderX,
            owner.Bottom + borderY);
    }

    internal static int EvaluateHit(
        NativePoint pointer,
        NativeRectangle bounds,
        int borderX,
        int borderY)
    {
        borderX = Math.Max(1, borderX);
        borderY = Math.Max(1, borderY);
        var x = pointer.X - bounds.Left;
        var y = pointer.Y - bounds.Top;
        var left = x < borderX;
        var right = x >= bounds.Width - borderX;
        var top = y < borderY;
        var bottom = y >= bounds.Height - borderY;
        if (left && top) return WindowFrameHitTest.TopLeft;
        if (right && top) return WindowFrameHitTest.TopRight;
        if (left && bottom) return WindowFrameHitTest.BottomLeft;
        if (right && bottom) return WindowFrameHitTest.BottomRight;
        if (left) return WindowFrameHitTest.Left;
        if (right) return WindowFrameHitTest.Right;
        if (top) return WindowFrameHitTest.Top;
        if (bottom) return WindowFrameHitTest.Bottom;
        return WindowFrameHitTest.Client;
    }

    private (int X, int Y) GetResizeBorder()
    {
        var dpi = NativeWindowMethods.GetDpiForWindow(_owner);
        if (dpi == 0) dpi = 96;
        return (
            Math.Max(1, NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxFrame, dpi)
                + NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxPaddedBorder, dpi)),
            Math.Max(1, NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCyFrame, dpi)
                + NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxPaddedBorder, dpi)));
    }

    private static bool IsResizeHit(int hit) =>
        hit is >= WindowFrameHitTest.Left and <= WindowFrameHitTest.BottomRight;

    private static void RegisterWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered) return;
            var windowClass = new NativeWindowClass
            {
                Size = (uint)Marshal.SizeOf<NativeWindowClass>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(SharedWindowProcedure),
                Instance = NativeWindowMethods.GetModuleHandle(null),
                ClassName = WindowClassName,
            };
            if (NativeWindowMethods.RegisterWindowClass(ref windowClass) == 0
                && Marshal.GetLastWin32Error() != ErrorClassAlreadyExists)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法注册 resize overlay 窗口类。");
            }
            _classRegistered = true;
        }
    }

    private static IntPtr WindowProcedure(
        IntPtr window,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter)
    {
        try
        {
            return Instances.TryGetValue(window, out var instance)
                ? instance.HandleMessage(message, wordParameter, longParameter)
                : NativeWindowMethods.DefWindowProc(window, message, wordParameter, longParameter);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return NativeWindowMethods.DefWindowProc(window, message, wordParameter, longParameter);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle == IntPtr.Zero) return;
        Instances.Remove(_handle);
        _ = NativeWindowMethods.DestroyWindow(_handle);
        _handle = IntPtr.Zero;
    }
}
