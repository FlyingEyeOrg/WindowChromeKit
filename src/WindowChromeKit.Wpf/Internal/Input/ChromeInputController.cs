using System.Runtime.InteropServices;
using System.Windows;
using WindowChromeKit.Wpf;

namespace WindowChromeKit.Wpf.Internal;

/// <summary>负责 ChromeWindow 的命中测试、非客户区鼠标跟踪和标题栏按钮按压状态机。</summary>
internal sealed class ChromeInputController
{
    private const uint TmeLeave = 0x00000002;
    private const uint TmeNonClient = 0x00000010;
    private readonly ChromeWindow _owner;
    private int _hotPart;
    private int _pressedPart;
    private bool _trackingCaptionButtonPress;
    private bool _trackingMouse;

    internal ChromeInputController(ChromeWindow owner) => _owner = owner;

    internal bool IsTrackingCaptionButtonPress => _trackingCaptionButtonPress;

    internal ChromeHitTestRole HotRole => NativePartToRole(_hotPart);

    internal ChromeHitTestRole PressedRole => NativePartToRole(_pressedPart);

    /// <summary>把屏幕命中点转换成 WM_NCHITTEST 使用的原生部件值。</summary>
    internal int HitTest(NativePoint pointer)
    {
        try
        {
            return RoleToNativePart(_owner.GetRoleAtPoint(pointer));
        }
        catch (InvalidOperationException)
        {
            return WindowFrameHitTest.Client;
        }
        catch (ArgumentException)
        {
            return WindowFrameHitTest.Client;
        }
    }

    /// <summary>ResizeMode 变化时清理已失效的最小化/最大化按钮状态。</summary>
    internal void PrepareResizeModeChange()
    {
        var canMinimize = _owner.ResizeMode != ResizeMode.NoResize;
        var canMaximize = _owner.IsResizable;
        var cancelPress = false;
        if (!canMinimize)
        {
            if (_hotPart == WindowFrameHitTest.MinButton)
                _hotPart = 0;
            cancelPress = _pressedPart == WindowFrameHitTest.MinButton;
        }
        if (!canMaximize)
        {
            if (_hotPart == WindowFrameHitTest.MaxButton)
                _hotPart = 0;
            cancelPress |= _pressedPart == WindowFrameHitTest.MaxButton;
        }
        if (cancelPress)
            CancelCaptionButtonPress();
        else
            _owner.ApplyVisualState();
    }

    /// <summary>处理非客户区按钮按下，必要时接管鼠标捕获。</summary>
    internal bool HandleNcLButtonDown(IntPtr window, int part)
    {
        var pressedRole = NativePartToRole(part);
        if (!IsCaptionButtonRole(pressedRole))
            return false;
        _hotPart = part;
        _pressedPart = _owner.IsRoleEnabled(pressedRole) ? part : 0;
        _trackingCaptionButtonPress = _pressedPart != 0;
        if (_trackingCaptionButtonPress)
            _ = NativeWindowMethods.SetCapture(window);
        _owner.ApplyVisualState();
        // 系统默认过程会独占跟踪非客户区按钮，导致自定义按钮收不到抬起消息。
        // 这里接管捕获，确保移出、移回、取消和最终命令具有普通 Button 一致的语义。
        return true;
    }

    internal bool HandleNcLButtonUp(int part)
    {
        if (!_trackingCaptionButtonPress)
            return false;
        CompleteCaptionButtonPress(part);
        return true;
    }

    internal bool HandleMouseMove()
    {
        if (!_trackingCaptionButtonPress)
            return false;
        UpdateCapturedCaptionButtonPointer();
        return true;
    }

    internal bool HandleLButtonUp()
    {
        if (!_trackingCaptionButtonPress)
            return false;
        var currentPart = GetCaptionButtonPartAtCursor();
        CompleteCaptionButtonPress(currentPart);
        return true;
    }

    internal void HandleCaptureChanged()
    {
        _trackingCaptionButtonPress = false;
        ResetPointerVisualState();
    }

    /// <summary>跟踪非客户区悬停部件，用来刷新标题栏按钮的 Hover 状态。</summary>
    internal void ObserveNonClientMove(int part)
    {
        var changed = _hotPart != part;
        _hotPart = part;
        if (!_trackingMouse)
        {
            var tracking = new NativeTrackMouseEvent
            {
                Size = Marshal.SizeOf<NativeTrackMouseEvent>(),
                Flags = TmeLeave | TmeNonClient,
                WindowHandle = _owner.FrameHandle,
            };
            _trackingMouse = NativeWindowMethods.TrackMouseEvent(ref tracking);
        }
        if (changed)
            _owner.ApplyVisualState();
    }

    /// <summary>鼠标被捕获时更新当前悬停的标题栏按钮。</summary>
    internal void UpdateCapturedCaptionButtonPointer()
    {
        var part = GetCaptionButtonPartAtCursor();
        if (_hotPart == part)
            return;
        _hotPart = part;
        _owner.ApplyVisualState();
    }

    /// <summary>读取光标位置并转换成原生标题栏按钮部件值；失败时返回 0。</summary>
    internal int GetCaptionButtonPartAtCursor()
    {
        if (!NativeWindowMethods.GetCursorPos(out var pointer))
            return 0;
        try
        {
            var role = _owner.GetRoleAtPoint(pointer);
            return IsCaptionButtonRole(role) ? RoleToNativePart(role) : 0;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
        catch (ArgumentException)
        {
            return 0;
        }
    }

    /// <summary>结束一次标题栏按钮按压，并在同一按钮上抬起时执行窗口命令。</summary>
    internal void CompleteCaptionButtonPress(int releasedPart)
    {
        var pressedPart = _pressedPart;
        _trackingCaptionButtonPress = false;
        _ = NativeWindowMethods.ReleaseCapture();
        _pressedPart = 0;
        _hotPart = releasedPart;
        _owner.ApplyVisualState();
        _ = _owner.TryExecuteCaptionButton(
            NativePartToRole(pressedPart),
            NativePartToRole(releasedPart)
        );
    }

    /// <summary>取消当前标题栏按钮按压并释放鼠标捕获。</summary>
    internal void CancelCaptionButtonPress()
    {
        var releaseCapture = _trackingCaptionButtonPress;
        _trackingCaptionButtonPress = false;
        if (releaseCapture)
            _ = NativeWindowMethods.ReleaseCapture();
        ResetPointerVisualState();
    }

    /// <summary>清空 hover/pressed 状态并刷新视觉层。</summary>
    internal void ResetPointerVisualState()
    {
        var changed = _hotPart != 0 || _pressedPart != 0;
        _trackingMouse = false;
        _hotPart = 0;
        _pressedPart = 0;
        if (changed)
            _owner.ApplyVisualState();
    }

    private int RoleToNativePart(ChromeHitTestRole role) =>
        role switch
        {
            ChromeHitTestRole.Caption => WindowFrameHitTest.Caption,
            ChromeHitTestRole.SystemMenu => WindowFrameHitTest.SystemMenu,
            ChromeHitTestRole.MinimizeButton when _owner.ResizeMode != ResizeMode.NoResize =>
                WindowFrameHitTest.MinButton,
            ChromeHitTestRole.MaximizeButton when _owner.ResizeMode != ResizeMode.NoResize =>
                WindowFrameHitTest.MaxButton,
            ChromeHitTestRole.CloseButton => WindowFrameHitTest.Close,
            _ => WindowFrameHitTest.Client,
        };

    private static ChromeHitTestRole NativePartToRole(int part) =>
        part switch
        {
            WindowFrameHitTest.Caption => ChromeHitTestRole.Caption,
            WindowFrameHitTest.SystemMenu => ChromeHitTestRole.SystemMenu,
            WindowFrameHitTest.MinButton => ChromeHitTestRole.MinimizeButton,
            WindowFrameHitTest.MaxButton => ChromeHitTestRole.MaximizeButton,
            WindowFrameHitTest.Close => ChromeHitTestRole.CloseButton,
            _ => ChromeHitTestRole.Default,
        };

    private static bool IsCaptionButtonRole(ChromeHitTestRole role) =>
        role
            is ChromeHitTestRole.MinimizeButton
                or ChromeHitTestRole.MaximizeButton
                or ChromeHitTestRole.CloseButton;
}
